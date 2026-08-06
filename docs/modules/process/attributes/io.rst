minIO
+++++

For each process group, you can set a cumulative storage IO threshold, so that only a group actually moving data counts as usage. The following formats are valid:

``100kb``
    A number with a binary unit declares an amount of bytes that have to be read from or written to storage during the configured timeout of the :doc:`/modules/system/monitor`, otherwise the process group will be considered idle.

``1MB/s``
    A number with both a binary unit and a time period declares an amount of bytes that have to be read from or written to storage **in average** during the configured timeout of the :doc:`/modules/system/monitor`, otherwise the process group will be considered idle.

A number without a binary unit (e.g. ``500`` or ``100/s``) is rejected here: on the network side it would mean raw packets, which processes cannot count.

Storage IO deliberately excludes network traffic — that is what ``minTraffic`` is for. What exactly is counted is each platform's closest honest answer:

- **Windows** counts file IO as the process performs it: reads served from the cache count in full, traffic through the socket APIs does not, and memory-mapped IO is invisible.
- **Linux** counts bytes the process moves to and from the storage layer (``read_bytes``/``write_bytes``): reads served from the page cache are invisible, buffered writes are credited to the writing process. When a watched process reaps a child, the child's lifetime totals fold into the parent in one jump, so the cycle after a child exits may report demand once too often.
- **macOS** counts physical disk reads (reads served from the cache are invisible — the platform keeps no logical read counter) and file writes to internal storage, both buffered and direct; on external volumes only direct and flushed writes are visible, not buffered writes as such.

A platform that keeps no such counter at all refuses ``minIO`` when the watch is built, naming the watch and the attribute, rather than carrying a threshold nothing measures. Where the counters exist but a single process will not answer for them — one that exited between two cycles, or one the service may not open — that process contributes nothing and the rest of the group is measured as usual; a cycle in which *no* process answered is read as demand, so a momentary gap can never be what puts the machine to sleep.

If several thresholds are configured, **all** of them have to be met for the group to count as active — unless ``min="or"`` is set, where any single one is enough.
