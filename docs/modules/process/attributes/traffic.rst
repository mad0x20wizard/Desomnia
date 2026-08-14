minTraffic
++++++++++

.. attention:: Windows and macOS only. Linux offers no per-process network counter, and configuring ``minTraffic`` where none exists is refused outright: the service reports the watch and the attribute, and does not start. A threshold nothing measures cannot be honoured either, and a machine guarded by an attribute nobody is watching is worse than one that said so.

For each process group, you can set a cumulative network traffic threshold, so that only a group actually transferring data over the network counts as usage.

Metered are actual TCP and UDP payload bytes per process — the same numbers the system's own tools show in their per-process network columns. Both platforms meter on demand: nothing is measured until a process a ``minTraffic`` threshold applies to is first sampled, and the metering stops when the last such process is gone. A configuration without ``minTraffic`` therefore pays nothing at all.

**Windows** reads them from a kernel event tracing (ETW) session. While it runs, the kernel reports every packet-sized network event on the machine — so the cost is continuous for exactly as long as a matching process runs.

**macOS** reads them from the kernel's own per-flow counters instead, asking once per monitor cycle rather than being told continuously; between cycles the metering costs nothing. Because there is no public interface for those counters, the daemon speaks the private one the system's tools use, and checks at startup that it still understands what the running kernel answers. Where it does not — a macOS whose layout this build has never seen — a configured ``minTraffic`` is refused like an unsupported platform, with the details in the log. Metering needs no additional privileges beyond those the daemon already runs with.

.. note:: On macOS, traffic belonging to a socket one process opened *on behalf of* another is counted for the process it was opened for, not for the one doing the transferring.

The formats match ``minIO``:

``100kb``
    Bytes that have to be received or sent during the configured timeout of the :doc:`/modules/system/monitor`, otherwise the process group will be considered idle.

``1MB/s``
    Bytes that have to be received or sent **in average** during the configured timeout of the :doc:`/modules/system/monitor`, otherwise the process group will be considered idle.

A number without a binary unit is rejected, as for ``minIO``.

If several thresholds are configured, **all** of them have to be met for the group to count as active — unless ``min="or"`` is set, where any single one is enough.
