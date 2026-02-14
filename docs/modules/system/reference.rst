Configuration reference
=======================

SystemMonitor
-------------

.. code:: xml

  <SystemMonitor version="1"  timeout="5min"

    onIdle="sleep"
    onDemand="sleepless"

    onSuspend=""
    onSuspendTimeout="restart"
    onResume="">

    <NetworkMonitor />
    <NetworkSessionMonitor />
    <SessionMonitor />

    <ProcessMonitor />
    <PowerRequestMonitor />

  </SystemMonitor>

timeout
+++++++

:duration:

This represents the duration for which all configured monitors must be idle to trigger the ``onIdle`` event. Since not all monitors may be able to determine a precise idle time, this is actually implemented as a periodic timer. Then the timer elapses and no monitor detects a usage, the action configured for the ``onIdle`` gets executed.

If you do not configure this option, no idle cheks will be performed whatsoever. As a consequence no nested monitor or resource will execute their ``onIdle`` actions ever. Some ``onDemand`` events may still be triggered, if the respective monitor has other means of detecting this.

onIdle
++++++

:⚡️ event:

The ``onIdle`` event of the ``<SystemMonitor>`` gets triggered, when none of the nested monitor reports a usage. This would be a good moment to suspend the computer. Alternatively sou can configure any of the other :doc:`available actions <actions>`.

onDemand
++++++++

:⚡️ event:

Each time when the ``timeout`` elapses and at least one nested monitor reports a usage, the ``onDemand`` action gets executed. To prevent the built-in power management from interfering with Desomnia's workings, it is recommended to set the system to ``sleepless``.

onSuspend
+++++++++

:⚡️ event:

This event handler will be executed each time the system suspends. It could be triggered by Desomnia or another source.

onSuspendTimeout
++++++++++++++++

:⚡️ event:

If you configure the ``sleep`` action and the system does not suspend until the next ``timeout`` this event handler will be executed. If a low-level component is preventing the computer from sleeping, you could, for example, restart the system to restore normal sleep behaviour.

onResume
++++++++

:⚡️ event:

This event handler will be executed when the system wakes up from sleep.


