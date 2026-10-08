Configuration
=============

SystemMonitor
-------------

.. code:: xml

  <SystemMonitor version="3" timeout="5min"
    keepDisplayAwake="false"

    onIdle="sleep"
    onUsage="sleepless"

    onSuspend=""
    onSuspendTimeout=""
    onResume="">

    <NetworkMonitor />
    <NetworkSessionMonitor />
    <SessionMonitor />

    <ProcessMonitor />
    <PowerRequestMonitor />

  </SystemMonitor>

See :doc:`/guides/sleep` for platform-specific monitoring examples.

.. _system-monitor-timeout:

timeout
+++++++

:⏱️ duration:

This represents the interval between activity inspections. Each monitor reports its current activity or activity since the previous check, depending on the resource. When the timer elapses and no monitor reports usage, the action configured for ``onIdle`` gets executed.

This is not a precise countdown from the last keyboard or network event. You can use a monitor's own threshold, such as ``maxLastInputTime``, when available, or add a delay to an idle action.

If you do not configure this option, no idle checks will be performed whatsoever. As a consequence no nested monitor or resource will execute their ``onIdle`` or ``onUsage`` actions. Capable resources may still trigger ``onDemand`` from an external request, such as an incoming request to a network host, service, or Duo instance.

onIdle
++++++

:⚡️ event:

The ``onIdle`` event of the ``<SystemMonitor>`` gets triggered when none of the nested monitors reports usage. This would be a good moment to suspend the computer. Alternatively, you can configure any of the other :doc:`available actions <actions>`.

The ``sleep`` action suspends the machine; ``sleep+10min`` waits for a further ten minutes of continued idle state. Activity cancels a pending idle action. See :doc:`/concepts/resources`.

onUsage
+++++++

:⚡️ event:

Each time the ``timeout`` elapses and at least one nested monitor reports activity, the ``onUsage`` action gets executed. To prevent the built-in power management from interfering with Desomnia's workings, you can configure ``sleepless`` to hold a power request while the monitored workloads are active.

.. _keep-display-awake:

keepDisplayAwake
++++++++++++++++

:default: ``false``

When true, the ``sleepless`` action also requests that the screen remain awake. It does not generate activity by itself.

On Windows, install the :doc:`Interactive Taskbar Icon </plugins/bridge>` component so the display request can be made in the local user's session. Without a console session, there is no local screen request to hold. macOS supports display keep-awake requests directly.

On Linux, this requests a logind idle inhibitor. Desktop display blanking is controlled by the display server or desktop environment, so the setting is not a guarantee that the screen stays on.

.. code:: xml

  <SystemMonitor version="3" timeout="2min" keepDisplayAwake="true"
                 onUsage="sleepless" onIdle="sleep">
    <PowerRequestMonitor />
  </SystemMonitor>

To make a connected display count as activity instead, use :doc:`/modules/display/monitor`.

onSuspend
+++++++++

:⚡️ event:

This event handler will be executed when Desomnia runs its ``sleep`` action, before requesting system suspension. It is not a general notification for suspensions initiated by the operating system, a user, or another program.

onSuspendTimeout
++++++++++++++++

:⚡️ event:

If you configure the ``sleep`` action and the system does not reach the suspended state, you can use this event handler to recover. For example, ``onSuspendTimeout="reboot+2min"`` requests a reboot if suspension has not occurred within two minutes. The built-in reboot action is named ``reboot``.

.. important::

  An explicit delay is required. This delay is independent of the global inspection interval. No recovery action is configured by default.

onResume
++++++++

:⚡️ event:

This event handler will be executed when the platform reports that the system has resumed. It accepts delayed actions. On macOS, background network wakes are distinct from a full, user-visible wake and do not necessarily produce this event.
