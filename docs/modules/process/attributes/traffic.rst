minTraffic
++++++++++

.. attention:: Windows only. No other platform offers a per-process network counter; there the threshold is ignored — a warning is logged once, and the group is treated as if the threshold were met, so the watch degrades to whatever the other attributes still measure.

For each process group, you can set a cumulative network traffic threshold, so that only a group actually transferring data over the network counts as usage. How the traffic is measured is chosen once, in the persistent configuration:

.. code:: xml

  <?global ProcessManager:watchTraffic="passive" ?>

Unlike the attributes on this page, this directive goes at the **top of the configuration file, outside the root element** — placed inside, the service refuses to start. It is read once at startup: changing it requires a service restart (editing it while the service runs deliberately triggers one).

``passive`` (default)
    The traffic is approximated from the kernel's per-process IO counters — one system call per watched process, no standing cost. The approximation is honest but rough: it counts all device-control IO of the process (which is where socket transfers land, but not only them), receives on the kernel's fast path escape it entirely, and it cannot tell sent from received. Configure thresholds with slack, and treat it as "this process is talking to the network" rather than as an exact meter.

``active``
    The traffic is metered from kernel event tracing (ETW): actual TCP and UDP payload bytes per process, the same source the Resource Monitor's per-process network column uses. Precise, at the price of the service watching every packet-sized kernel event on the machine while process watches are active. A process the meter has never seen transfer anything answers from the passive approximation instead.

The formats match ``minIO``:

``100kb``
    Bytes that have to be received or sent during the configured timeout of the :doc:`/modules/system/monitor`, otherwise the process group will be considered idle.

``1MB/s``
    Bytes that have to be received or sent **in average** during the configured timeout of the :doc:`/modules/system/monitor`, otherwise the process group will be considered idle.

A number without a binary unit is rejected, as for ``minIO``.

If several thresholds are configured (``minCPU``, ``minIO``, ``minTraffic``), **all** of them have to be met for the group to count as active.
