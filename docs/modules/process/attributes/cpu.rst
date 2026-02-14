minCPU
++++++

When using Desomnia as a :doc:`local resource manager </guides/sleep>`, you can set the cumulative CPU threshold that counts as usage. The following formats are valid:

``10%``
    When a number is configured with a percentage unit, the absolute processing time will be compared with the total processing time available since the last timeout. The used processing time has to be more than this average for the resource to be considered non-idle. The total processing time takes into account the number of available processing cores, so the value will correspond to what you see in Task Manager.

``10min``
    When a number is configured with a time unit, it is compared to the absolute processing time since the last timeout. If multiple cores are used, the processing time can exceed the elapsed clock time by a factor equal to the number of installed cores. You can use the usual format for durations.
