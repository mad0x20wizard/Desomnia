Demand detection
================

When a Moonlight client tries to reach an instance that is not running,
Desomnia can start it through ``onInstanceDemand="start"``. This incoming
request is separate from the periodic activity checks that decide when an
instance is idle.

The examples below start instances on demand and stop them when idle. They
also put the physical machine to sleep when all configured monitors are idle.
Add other monitors if the machine runs additional workloads that should keep
it awake. Remove ``onIdle="sleep"`` from ``SystemMonitor`` to manage only
the Duo instances.

Packet capture
--------------

With a ``NetworkMonitor`` configured, Desomnia uses packet capture to observe
connection attempts to each instance's service. Npcap must be installed and the
network monitor must cover the interface carrying the client's traffic.

.. code:: xml

   <SystemMonitor version="3" timeout="5min" onUsage="sleepless" onIdle="sleep">
     <SessionMonitor />
     <NetworkMonitor />
     <DuoSessionMonitor onInstanceDemand="start" onInstanceIdle="stop" />
   </SystemMonitor>

Capture also supports ``minInstanceStreamTraffic`` and per-instance
``minStreamTraffic`` thresholds. These settings cannot be used in listener
mode.

Per-instance :ref:`HostFilterRule <duo-instance-host-filter-rule>` and
:ref:`HostRangeFilterRule <duo-instance-host-range-filter-rule>` elements can
restrict which clients contribute network demand and streaming activity.

TCP listeners
-------------

Without a ``NetworkMonitor``, or with ``useListener="true"`` on
``DuoSessionMonitor``, Desomnia listens on the base ports of stopped
instances. It releases the listener when the instance starts and resumes
listening when it stops. Active connections are observed through Windows.

.. code:: xml

   <SystemMonitor version="3" timeout="5min" onUsage="sleepless" onIdle="sleep">
     <SessionMonitor />
     <DuoSessionMonitor useListener="true"
                        onInstanceDemand="start" onInstanceIdle="stop" />
   </SystemMonitor>

Listener mode is unavailable for sandboxed instances. It also cannot measure
streaming byte rates, so configuring a streaming-traffic threshold causes an
error. Input and process metrics remain available through the session monitor.

Instance host and host-range filters also require packet capture and are
rejected in listener mode.

Desomnia adds the required Windows Firewall inbound rules while the listeners
are in use and removes them when it shuts down.

See :doc:`config` for activity settings and
:doc:`/guides/troubleshooting` if instances do not start or stop as expected.
