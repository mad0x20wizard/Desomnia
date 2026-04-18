Resources and Events
====================

Desomnia models your system as a tree of logical resources. The ``<SystemMonitor>`` sits at the root and represents the system as a whole. Below it, monitors each track a specific type of activity and contribute their state upward.

The monitoring tree
-------------------

There are two kinds of monitors. Both contribute to the system's idle state, but they differ in whether they maintain persistent state for their resources.

Resource monitors
+++++++++++++++++

Resource monitors form a recursive tree. Each can contain other resource monitors or individual resource nodes, and is itself treated as a resource by its parent — meaning its state propagates upward like any other node::

    SystemMonitor
    ├── SessionMonitor              (resource monitor)
    │   └── Session "1"             (resource monitor)
    │       └── Process "vlc"       (resource)
    ├── NetworkMonitor              (resource monitor)
    │   └── RemoteHost "server"     (resource)
    └── ProcessMonitor              (resource monitor)
        └── Process "notepad"       (resource)

Only resource monitors can have children. Leaf resources report their activity state directly; monitors derive their state by aggregating their children — a monitor is active if at least one child is active. Resource monitors track a persistent idle/active state and expose ``onIdle`` and ``onDemand`` events.

Monitors
++++++++

Some monitors do not maintain a persistent idle/active state for their resources and do not expose ``onIdle`` or ``onDemand``. They simply report whether relevant activity is currently present:

``NetworkSessionMonitor``
    Tracks open SMB sessions from remote clients. Each check queries the current set of open sessions; there is no persistent per-session state between polls.

``PowerRequestMonitor``
    Tracks active power requests issued by processes and drivers. These are exposed by Windows only through the output of ``powercfg /requests`` — there is no API that provides persistent state or change notifications for individual requests. As a result, it is not meaningful to speak of a power request becoming "idle" or "active" in the resource-monitor sense.

Example
+++++++

A corresponding configuration, that makes use of all these types, could look like the following:

.. code:: xml

    <?xml version="1.0" encoding="utf-8"?>
    <SystemMonitor version="1" timeout="2min" onIdle="sleep" onDemand="sleepless">

        <SessionMonitor>
            <User name="John">
                <Process name="VideoLAN">vlc</Process>
            </User>
        </SessionMonitor>

        <NetworkMonitor>
            <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" />
        </NetworkMonitor>

        <ProcessMonitor>
            <Process name="Notepad">notepad</Process>
        </ProcessMonitor>

        <NetworkSessionMonitor />
        <PowerRequestMonitor />

    </SystemMonitor>

The system is considered active as long as at least one monitor reports activity. When every monitor is idle, the system becomes idle and any configured action fires.

Idle detection
--------------

Desomnia polls for activity. Every ``timeout`` interval, all monitors are asked for their current state. A monitor is active if any of the activities it tracks are ongoing or were observed since the last poll.

.. include:: /concepts/time.rst

Events
------

Configuration attributes that respond to state changes use the ``on`` prefix followed by the event name. The attribute value specifies the action to execute.

onIdle
++++++

:⚡️ event:

Fires when a resource has been idle for a complete timeout cycle.

.. code:: xml

    <SystemMonitor version="1" timeout="2min" onIdle="sleep">

onDemand
++++++++

:⚡️ event:

Fires when a resource transitions from idle to active. For resources that can detect activity without polling — such as network connections or incoming service requests — Desomnia fires this event immediately when the activity is observed. For other resources, the transition is detected at the next timeout cycle.

.. code:: xml

    <SystemMonitor version="1" timeout="2min" onIdle="sleep" onDemand="sleepless">

With both events configured, Desomnia holds a *sleepless* power request while any monitor is active, and releases it — then puts the system to sleep — once everything goes quiet.

Delayed actions
---------------

An action can be delayed so that a brief period of inactivity does not immediately trigger it.

Time-based delay
++++++++++++++++

The action fires only if the resource remains idle throughout the delay after idle was first detected:

.. code:: xml

    onIdle="sleep+10min"

Cycle-based delay
+++++++++++++++++

The action fires after the resource has been idle for the specified number of consecutive timeout cycles:

.. code:: xml

    onIdle="sleep+2x"

``+0x`` means no delay; ``+1x`` means execute on the next inspection after idle was first detected; ``+2x`` means one inspection after that, and so on.

.. note::

    Prefer cycle-based delays over short time-based ones. A time-based delay is measured from when idle was first detected; if the delay expires during a timeout phase where some activity was present, the action may fire unexpectedly. Cycle-based delays avoid this by anchoring to inspection boundaries.

If activity resumes before the delay expires, the pending action is cancelled and the counter resets.

Partial idleness
----------------

The system is idle only when every monitor is idle, but individual monitors and their resources can become idle independently. Their ``onIdle`` actions fire as soon as that branch goes quiet, even if the system as a whole is still active.

For example, a user session may go idle while an open SMB connection is still keeping the system busy:

.. code:: xml

    <SystemMonitor version="1" timeout="2min" onIdle="sleep" onDemand="sleepless">

        <SessionMonitor>
            <User name="John" onIdle="logout" />
        </SessionMonitor>

        <NetworkSessionMonitor />

    </SystemMonitor>

When John's session goes idle, the ``<User>`` resource fires its ``onIdle="logout"`` action — without affecting the active SMB session. The system-level ``onIdle="sleep"`` fires only once both the session and the network session are idle.

Hierarchical action resolution
-------------------------------

Actions are resolved up the tree: a resource or monitor can reference actions defined by any of its ancestors. The ``<SystemMonitor>`` defines several actions — such as ``sleep``, ``shutdown``, and ``exec`` — that are available to every node in the tree, no matter how deeply nested.

A practical use of this is the ``exec`` action, which runs an arbitrary command. Any monitor or resource can trigger it:

.. code:: xml

    <SystemMonitor version="1" timeout="2min" onIdle="sleep">

        <SessionMonitor>
            <User name="John" onIdle="exec('C:\scripts\notify.ps1')" />
        </SessionMonitor>

    </SystemMonitor>

When John's session goes idle, the script runs — even though ``exec`` is defined at the ``<SystemMonitor>`` level. See :doc:`/modules/system/actions` for the full list of actions and their parameters.

Resource-specific events
-------------------------

Some monitors expose additional events beyond ``onIdle`` and ``onDemand``. For example, the ``<User>`` resource supports ``onLogin``, ``onLogout``, ``onDisconnect``, and others. These are documented alongside their respective monitors. The available actions for any event depend on the monitor type and its position in the tree.
