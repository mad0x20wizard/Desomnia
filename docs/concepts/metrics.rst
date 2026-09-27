Observable Metrics
==================

Metrics describe the measurable activity of processes and user sessions.
Each metric supplies a Boolean value to a ``watch`` expression. Thresholds
determine when measured activity counts as usage; the expression determines
which combinations keep the resource active.

.. toctree::
   :maxdepth: 1

   metrics/index
   metrics/watch

Thresholds
----------

If not specified otherwise, the thresholds apply to the 
combined counters of all matching processes.
On a ``<Process>`` element, ``watchChildren="true"`` includes child
processes. Using it on a :doc:`session selector </modules/session/config>`
it applies to all of the session's processes.

Inspection interval
+++++++++++++++++++

Thresholds are evaluated over the inspection interval set by
:ref:`SystemMonitor timeout <system-monitor-timeout>`. A rate such as ``1MB/s`` is an average over
that interval. An absolute value such as ``10ms`` or ``100kb`` applies
to the amount observed during the interval.

Calibration
+++++++++++

The measured metric values are usually equivalent to those, that your platform's 
Task-Manager or Activity Monitor displays.

Setting any threshold (like ``minCPU``, ``minGPU``, ``minIO``, or ``minTraffic``)
to ``0`` does not constrain the detected usage of a resource,
but enables the measurement of the respective metric,
which value you can then read via the log.

All configured threasholds are measured, including those not referenced
by any expression. Using an unsupported or unknown metric is not allowed.
If no monitored process can supply a watched metric during
inspection, an error is reported instead of assuming activity or
inactivity. See :doc:`/guides/troubleshooting`.
