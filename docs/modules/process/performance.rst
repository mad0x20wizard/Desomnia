Performance
===========

The Process Monitor needs to know when processes start and stop. There are two ways to obtain this information: polling the OS for a current process list at a fixed interval, or receiving push notifications from the OS the moment a change occurs. Which mechanism is used depends on the platform.

Polling
-------

:OS: 🪐 *Platform-independent*

The fallback implementation polls the OS process list at a configurable interval using standard .NET abstractions, which makes it work on every supported platform. The trade-off is CPU overhead proportional to the polling frequency: every tick, Desomnia enumerates all running processes and compares the result against the previously known state.

This is what runs wherever a platform offers nothing better. Windows replaces it with event notifications, and macOS and Linux each with a native enumeration — all described below.

The default ``pollInterval`` is ``2s``, which is adequate for most home-lab workloads where a second or two of detection latency is acceptable.

.. include:: attributes/poll.rst

Event Trace for Windows (ETW)
------------------------------

:OS: 🪟 *Windows*

On Windows, Desomnia uses the `Event Trace for Windows <https://learn.microsoft.com/en-us/windows/win32/etw/about-event-tracing>`__ (ETW) API instead of polling. ETW allows Desomnia to subscribe to process start and stop events at the kernel level, receiving notifications in near real time with no periodic overhead. ETW is activated automatically when running on Windows; no configuration is required.

A trace event names the process it reports, so nothing further is asked about it. The processes that *are* looked up one at a time — the ancestors ``watchChildren`` walks, and anything Desomnia is handed a process id for — are asked of the kernel directly rather than through the .NET process list, which describes every process on the machine to answer about one. That difference is measurable: a few tens of microseconds against a few milliseconds each.

Native process enumeration
--------------------------

:OS: 🍎 *macOS*

macOS does not provide a reliable mechanism for receiving push notifications when arbitrary processes start or stop without additional entitlements. The `Endpoint Security API <https://developer.apple.com/documentation/endpointsecurity>`__, which would allow this, requires explicit approval from Apple and is not available to general-purpose software. Desomnia therefore polls on macOS, and ``pollInterval`` controls how often.

What it does not do is poll *through the .NET process list*. Asking .NET for the running processes on macOS makes it describe every single one of them before it will report so much as a process id — including one kernel call for every thread of every process. On a desktop with a few hundred processes that is several thousand system calls per tick, and it was by a wide margin the most expensive thing the daemon did.

Instead, Desomnia asks the kernel directly through ``libproc``. A tick that finds nothing new is a single ``proc_listallpids`` call returning a plain list of process ids; only ids that were not there a moment ago are looked up individually. A process' executable path and its processor time are read lazily, so a process nobody has a threshold or a path pattern for is never asked either. Watching processes on macOS is therefore close to free between changes, and ``pollInterval`` can be lowered for faster detection without the overhead that used to imply.

The same lookup reports each process' parent, which the .NET abstraction has no cross-platform way to expose — so ``watchChildren`` works on macOS just as it does on Windows.

Process *exits*, unlike starts, need no polling at all. macOS will report those on request through ``kqueue``, for any process and without an entitlement, so Desomnia asks the kernel to tell it when a watched process ends and hears about it the moment it happens rather than up to ``pollInterval`` later. This also covers the one case an enumeration gets wrong: a process that exits under a parent which never collects it stays in the kernel's process list until it is cleaned up, and only the exit notification says otherwise.

Graphics processor accounting
-----------------------------

:OS: macOS

.. include:: attributes/gpu.rst

Native process enumeration
--------------------------

:OS: 🐧 *Linux*

Linux has the same problem and the same answer. Asking .NET for the running processes parses ``/proc/[pid]/stat``, ``/proc/[pid]/status`` and ``/proc/[pid]/cmdline`` for every process, and then a stat file for every *thread* of every process — on a busy machine, thousands of file reads per tick.

Desomnia reads ``/proc`` directly instead. A tick is one directory listing, because on Linux the process table *is* a directory; only ids that were not there a moment ago are looked up, and the single ``/proc/[pid]/stat`` line that describes them carries the name, the parent and the session together. As on macOS, the executable path and the processor time are read only when something asks for them, and ``watchChildren`` works because the parent comes for free.

Process names longer than 15 characters are reported in full, which the kernel's own command field cannot do — the name is taken from the executable where that confirms the truncated one, and left alone where a process has deliberately renamed itself.

Process *exits* need no polling here either. Linux can hand out a descriptor that refers to a process and becomes readable the moment that process ends — for any process, with no privilege required — so Desomnia watches the processes it tracks and hears about an exit immediately rather than up to ``pollInterval`` later. As on macOS, this also covers the case an enumeration gets wrong: a process that exits under a parent which never collects it stays listed until it is cleaned up, and only the exit notification says otherwise.

That costs one descriptor per watched process. Desomnia stops opening them well short of the usual limit and falls back to polling for the rest, saying so in the log once when it does; a machine running many processes may want a raised ``LimitNOFILE`` in the service unit.

Linux also offers a second event source, the netlink process connector, which reports process *starts* as well. Desomnia does not use it: it is absent from the Raspberry Pi kernels entirely, and on the distributions inside the supported range it cannot filter server-side, so subscribing would mean waking for every fork on the machine — a poor trade for a daemon whose whole purpose is to let a machine be idle.

