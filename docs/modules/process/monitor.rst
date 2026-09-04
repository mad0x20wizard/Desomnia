Process Monitor
===============

:OS: 🪐 *Platform-independent*

The Process Monitor watches running OS processes and reports system activity as long as matching processes are alive and busy. Each ``<Process>`` entry defines a logical group: the text node is a regular expression matched against process image names, and enabling ``watchChildren`` extends the group to include any processes spawned by a match. This makes it straightforward to track browsers or IDE families where work happens across several child processes under a common parent.

Optionally, activity thresholds can be configured so that processes that are running but idle — sitting in the background with negligible activity — do not prevent sleep: CPU (``minCPU``), GPU (``minGPU``), storage IO (``minIO``), and per-process network traffic (``minTraffic``), where supported by the platform. The ``watch`` expression combines their Boolean results with ``and``, ``or``, and parentheses. Once the expression becomes false, the ``onIdle`` event fires for that group. ``onStart`` and ``onStop`` fire when the first process in a group appears or the last one exits.

The :doc:`available actions <actions>` cover forceful process termination with an optional graceful shutdown timeout. The :doc:`configuration reference <config>` documents all attributes. How Desomnia learns about process changes differs per platform — ETW event notifications on Windows, a native kernel enumeration on macOS and Linux, and generic polling elsewhere; the :doc:`performance <performance>` page explains the difference and how to configure the poll interval if needed.

.. toctree::
   actions
   config
   performance
