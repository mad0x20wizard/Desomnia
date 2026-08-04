pollInterval
++++++++++++

:⏱️ duration:
:default: ``2s``

Sets the interval at which the Process Monitor polls the OS for process changes.

On Windows, process changes are delivered by ETW event notifications and this setting has no effect.

On macOS and Linux, where no event-based alternative is used, this is the detection latency for a process starting. A tick costs a single system call there, so short intervals are affordable.
