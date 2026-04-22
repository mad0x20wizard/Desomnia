Process Monitor
===============

:OS: 🪐 *Platform-independent*

The Process Monitor watches running OS processes and reports system activity as long as matching processes are alive and busy. Each ``<Process>`` entry defines a logical group: the text node is a regular expression matched against process image names, and enabling ``watchChildren`` extends the group to include any processes spawned by a match. This makes it straightforward to track browsers or IDE families where work happens across several child processes under a common parent.

Optionally, a CPU threshold (``minCPU``) can be configured so that processes that are running but idle — sitting in the background with negligible CPU usage — do not prevent sleep. When CPU use drops below the threshold, the ``onIdle`` event fires for that group; ``onStart`` and ``onStop`` fire when the first process in a group appears or the last one exits.

The :doc:`available actions <actions>` cover forceful process termination with an optional graceful shutdown timeout. The :doc:`configuration reference <config>` documents all attributes. On Windows, Desomnia uses the ETW API for near real-time process event notifications instead of polling; the :doc:`performance <performance>` page explains the difference and how to configure the poll interval if needed.

.. toctree::
   actions
   config
   performance
