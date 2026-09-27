minGPU
++++++

The cumulative graphics-processing threshold for a process group, including
watched children. It accepts the same percentage and duration forms as
``minCPU``:

``10ms``
    More than ten milliseconds of GPU processing during the inspection interval.

``10%``
    GPU processing time greater than ten percent of the interval. One graphics
    engine busy for the full interval is 100%; simultaneous engines can total
    more than 100%. This is not normalized across CPU cores and need not match
    a utilization percentage shown by another monitoring application.

Use ``watch="CPU or GPU"`` when either CPU or graphics work should count.
If several thresholds are supplied without a custom expression, a process
uses AND by default.

A threshold of ``0`` always matches when the counter is readable. It does
not disable GPU collection. GPU thresholds are unavailable on Linux, and
availability on Windows/macOS depends on the hardware and platform counters.
Unsupported configured measurements are reported as configuration errors.

For the macOS accounting setting, see :doc:`/modules/process/performance`.
