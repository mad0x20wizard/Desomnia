Configuration
=============

SessionMonitor
--------------

:OS: 🪟 *Windows*

You can configure any number of selectors to watch OS user sessions. If multiple selectors target the same session — for example, if you configure actions that target all users and all administrators — the watched session will include the configured actions for both groups.

.. code:: xml

  <SystemMonitor version="3" timeout="2min" onUsage="sleepless" onIdle="sleep">

    <SessionMonitor
      watchInput="true"
      watchInputRemote="false"
      watchInputDisconnected="false"
      onUsage=""
      onIdle="">

      <User ... />
      <Administrator ... />

      <Everyone ... />

    </SessionMonitor>

  </SystemMonitor>

With no selectors, sessions are still watched using the normal input defaults. Use ``Everyone``, ``User``, or ``Administrator`` to apply options and actions.

.. include:: options/clock.rst

onUsage
+++++++

:⚡️ event:

This event is triggered on every inspection cycle in which at least one watched session reports activity.

onIdle
++++++

:⚡️ event:

This event is triggered when every watched session is idle.

User
----

All users, identified by their name, will be matched by this selector. The input options inherit their defaults from ``<SessionMonitor>``.

.. code:: xml

  <User name="Smith"

    watchInput="true"
    watchInputRemote="false"
    watchInputDisconnected="false"

    maxLastInputTime="20min"

    onIdle="logout"
    onLock=""
    onUnlock=""
    onLogin=""
    onRemoteLogin=""
    onConsoleLogin=""
    onConsoleConnect=""
    onRemoteConnect=""
    onDisconnect=""
    onLogout="">

    <Process ... />

  </User>

name
++++

:🔍 regex:

The value of this attribute is interpreted as a regular expression which is compared with the actual user name of the session. Only matching sessions will be watched with the configured actions. If you omit the ``name`` attribute, all user sessions are selected.

.. note::

  Selectors apply in this order: ``Everyone``, matching ``User`` entries in file order, then ``Administrator``. Their actions accumulate. Disabling ``watchInput`` in any matching selector disables input tracking for that session; remote and disconnected input tracking are enabled if any applied options enable them. A later nonempty ``maxLastInputTime`` replaces an earlier one, rather than the longest duration winning.

  Process-metric thresholds use the later supplied value. Watch expressions follow the :doc:`composition rules </concepts/metrics/watch>`.

.. include:: options/clock.rst

minCPU
++++++

The cumulative CPU threshold across the session's processes. See :doc:`/modules/process/attributes/cpu` for supported formats.

minGPU
++++++

The cumulative GPU threshold across the session's processes. See :doc:`/modules/process/attributes/mingpu` for supported formats and hardware requirements.

minIO
+++++

The cumulative storage I/O threshold across the session's processes. See :doc:`/modules/process/attributes/io` for supported formats.

minTraffic
++++++++++

The cumulative network traffic threshold across the session's processes. See :doc:`/modules/process/attributes/traffic` for supported formats.

watch
+++++

:default: ``default OR``

Combines ``Input`` and configured process metrics in an activity expression. By default, any supplied metric can count as activity. See :doc:`/concepts/metrics/watch` for syntax, available metrics, and composition.

.. code:: xml

  <SystemMonitor version="3" timeout="2min" onUsage="sleepless" onIdle="sleep">
    <SessionMonitor>
      <User name="^Smith$" watch="Input or IO" minIO="1MB/s"
            maxLastInputTime="10min" />
    </SessionMonitor>
  </SystemMonitor>

This session remains active after recent input or sufficient storage activity. A disconnected session can still report process activity even when ``watchInputDisconnected="false"``.

Nested process watches are another independent source of activity: if any nested ``Process`` is active, the containing session is active even when its own metric expression is false. See :doc:`/concepts/metrics` for examples and :doc:`/modules/process/config` for thresholds.

onIdle
++++++

:⚡️ event:

This event is triggered when inspection finds the session idle according to its configured metrics and nested process watches. An input timeout alone does not imply that a session doing background work is idle. Delayed actions are cancelled if activity resumes. Available :doc:`session actions <actions>` include ``lock``, ``disconnect``, and ``logout``.

onLogin
+++++++

:⚡️ event:

This event is triggered when a user logs in. The corresponding console or remote login event is also triggered.

onConsoleLogin
++++++++++++++

:⚡️ event:

This event is triggered when a user logs into the console session.

onRemoteLogin
+++++++++++++

:⚡️ event:

This event is triggered when a user logs in remotely.

onConsoleConnect
++++++++++++++++

:⚡️ event:

This event is triggered when the watched session is connected to the console session, including reconnections.

onRemoteConnect
+++++++++++++++

:⚡️ event:

This event is triggered when the watched session is connected to a remote client, including reconnections.

onDisconnect
++++++++++++

:⚡️ event:

This event is triggered when the user disconnects from the watched session. Disconnection is not logout: processes may continue running.

onLock
++++++

:⚡️ event:

This event is triggered when the session is locked.

onUnlock
++++++++

:⚡️ event:

This event is triggered when the session is unlocked.

onLogout
++++++++

:⚡️ event:

This event is triggered when the user logs out and the watched session ends.

Process
-------

The configuration of ``<Process>`` groups is the same as described in :doc:`/modules/process/config`, except that these processes will only be considered in the context of the specified session and have some additional events to configure:

.. code:: xml

  <Process name="VLC Media Player" ...

    onSessionUsage=""
    onSessionIdle="stop"
    onSessionConsoleConnect=""
    onSessionRemoteConnect=""
    onSessionDisconnect="">

    vlc

  </Process>

In this example, a running VLC process also keeps the session active. Activity thresholds are required to distinguish an idle process from one performing work. Nested process activity keeps the containing session active independently of its own ``watch`` expression.

onSessionUsage
++++++++++++++

:⚡️ event:

This event is triggered on every inspection cycle in which the containing session reports activity.

onSessionIdle
+++++++++++++

:⚡️ event:

This event is triggered when the session starts to idle.

onSessionConsoleConnect
+++++++++++++++++++++++

:⚡️ event:

This event is triggered when the session is connected to the console.

onSessionRemoteConnect
++++++++++++++++++++++

:⚡️ event:

This event is triggered when the session is connected to a remote client.

onSessionDisconnect
+++++++++++++++++++

:⚡️ event:

This event is triggered when the session is disconnected.

Administrator
-------------

All users with administrative permissions will be matched by this selector, so you cannot configure the ``name`` attribute here.

Apart from that everything is configured exactly the same as ``<User>``.

Group
-----

Only users that belong to the specified group, identified by its name, will be matched by this selector.

.. admonition:: Work in progress

  Group selection is not currently supported.

Everyone
--------

All users will be matched by this selector, so you cannot configure the ``name`` attribute here. Apart from that, everything is configured exactly the same as ``<User>``.
