Performance
===========

The Process Monitor needs to know when processes start and stop. There are two ways to obtain this information: polling the OS for a current process list at a fixed interval, or receiving push notifications from the OS the moment a change occurs. Which mechanism is used depends on the platform.

Polling
-------

:OS: 🪐 *Platform-independent*

The default implementation polls the OS process list at a configurable interval using standard .NET abstractions, which makes it work on every supported platform. The trade-off is CPU overhead proportional to the polling frequency: every tick, Desomnia enumerates all running processes and compares the result against the previously known state.

The default ``pollInterval`` is ``2s``, which is adequate for most home-lab workloads where a second or two of detection latency is acceptable.

.. include:: attributes/poll.rst

.. code:: xml

   <ProcessMonitor pollInterval="5s">
     <Process name="Browser">chrome|edge|firefox</Process>
   </ProcessMonitor>


Event Trace for Windows (ETW)
------------------------------

:OS: 🪟 *Windows*

On Windows, Desomnia uses the `Event Trace for Windows <https://learn.microsoft.com/en-us/windows/win32/etw/about-event-tracing>`__ (ETW) API instead of polling. ETW allows Desomnia to subscribe to process start and stop events at the kernel level, receiving notifications in near real time with no periodic overhead. ETW is activated automatically when running on Windows; no configuration is required.

macOS
-----

:OS: 🍎 *macOS*


macOS does not provide a reliable mechanism for receiving push notifications when arbitrary processes start or stop without additional entitlements. The `Endpoint Security API <https://developer.apple.com/documentation/endpointsecurity>`__, which would allow this, requires explicit approval from Apple and is not available to general-purpose software. Polling is therefore the only viable approach on macOS, and ``pollInterval`` controls its frequency.

