Configuration
=============

To enable the plugin, add a ``<DuoSessionMonitor>`` to your configuration. The plugin reads the available instances from the Duo Manager, so no individual instance configuration is required to get started.

DuoSessionMonitor
-----------------

:OS: 🪟 *Windows*

You must also configure a ``<SessionMonitor>`` alongside ``<DuoSessionMonitor>``. A complete configuration example is provided in :doc:`plugin`.

.. code:: xml

  <SystemMonitor version="3" timeout="5min" onUsage="sleepless" onIdle="sleep">

    <SessionMonitor /> <!-- mandatory -->
    <NetworkMonitor /> <!-- optional -->

    <DuoSessionMonitor serviceName="DuoService"
      watchMode="Auto"
      watchStreamTraffic="true"
      onUsage="sleepless"
      onIdle=""

      onInstanceUsage=""
      onInstanceDemand="start"
      onInstanceIdle="stop"

      onInstanceLogin=""
      onInstanceStart=""
      onInstanceStop=""
      onInstanceLogout="">

      <Instance name="Neo" ... />
      <Instance name="Thomas Anderson" ... />

    </DuoSessionMonitor>

  </SystemMonitor>

.. attention::

  None of the events has a default value, so you have to be explicit here. The ``onInstance...`` attributes provide defaults for instances without an explicit override.

serviceName
+++++++++++

:default: ``DuoService``

The name of the Duo service as it appears in the Windows Service Control Manager (SCM). This must match exactly for Desomnia to track the service lifecycle.

.. _duo-watch-mode:

watchMode
+++++++++

:default: ``Auto``

Selects the sources Desomnia uses to track Duo instances and their streaming services. You can combine flags with ``|``, for example ``Registry|EventLog|Polling|Capture``. Names are case-insensitive.

``Registry``
  Watches Duo instance registry changes to associate started instances with their Windows sessions.

``EventLog``
  Watches Duo start, stop, and error notifications in the Windows Event Log. This source is available with Duo 1.5.7 and later; Duo 1.6.1 and later use the dedicated Duo event log and identify instances more precisely. On earlier versions, this source is not enabled.

``Polling``
  Periodically queries instance state through the Duo web interface. You must also configure ``pollInterval`` to enable this source.

``Listener``
  Uses TCP listeners to detect requests to stopped instances and Windows connection information to observe active connections. This selects listener mode even when a ``<NetworkMonitor>`` is configured. See :doc:`demand` for its limitations.

``Capture``
  Uses packet capture through ``<NetworkMonitor>`` for network demand and streaming activity. At least one ``<NetworkMonitor>`` must be configured, and Npcap must be installed.

``Auto``
  Selects ``Registry`` and, on supported Duo versions, ``EventLog``. It also enables ``Polling`` when ``pollInterval`` is supplied. For traffic, it selects packet capture when a ``<NetworkMonitor>`` is configured, and TCP listeners otherwise.

You can combine any of ``Registry``, ``EventLog``, and ``Polling``; the enabled sources run together. Selecting one or more of these flags limits instance-state watching to those sources. If none is specified, their automatic selection still applies, including when you specify only ``Listener`` or ``Capture``. Windows session tracking remains active in all modes, so ``<SessionMonitor>`` is always required.

The traffic source is selected independently. If neither ``Listener`` nor ``Capture`` is specified, the presence of ``<NetworkMonitor>`` determines which one is used. This choice is based on configuration: a failed packet-capture setup does not switch to TCP listeners.

.. attention::

  ``Listener`` and ``Capture`` cannot be combined. An explicit ``Capture`` without a ``<NetworkMonitor>`` is also a configuration error.

For example, you can combine registry and event-log notifications with periodic polling and packet capture:

.. code:: xml

  <DuoSessionMonitor watchMode="Registry|EventLog|Polling|Capture"
    pollInterval="2s"
    onInstanceDemand="start"
    onInstanceIdle="stop" />

This example requires ``<SessionMonitor>`` and ``<NetworkMonitor>`` alongside the Duo monitor. Selecting only ``Polling|Listener`` instead uses polling for instance state and TCP listeners for demand detection.

pollInterval
++++++++++++

:⏱️ duration:
:default: *not set*

The interval between periodic instance-state requests to the Duo web interface. Polling runs only when this value is supplied and ``watchMode`` selects ``Polling``, either explicitly or through automatic instance-state selection. For example, ``pollInterval="2s"`` requests a check every two seconds.

Without a value, periodic polling is disabled, even with ``watchMode="Polling"``. Supplying a value does not enable polling when explicit instance-state flags exclude ``Polling``, such as ``watchMode="Registry|EventLog"``. Periodic activity inspection uses ``SystemMonitor timeout`` independently.

watchStreamTraffic
++++++++++++++++++

:default: ``true``

Makes each instance's monitored streaming service available as the ``StreamTraffic`` activity metric. Disabling this setting excludes streaming activity from the instance's expression. Input and process metrics remain available, and demand detection can still start the instance.

minInstanceStreamTraffic
++++++++++++++++++++++++

Optional default traffic threshold for all instances, expressed in the :doc:`network traffic formats </modules/network/attributes/traffic>`. For example, ``1MB/s`` requires that average traffic rate during the inspection interval.

This measures traffic at the streaming service, not the session's ``minTraffic`` per-process measurement. It requires packet capture and cannot be used with listener mode.

onUsage
+++++++

:⚡️ event:

This event is triggered on every inspection cycle in which at least one Duo instance reports activity according to its configured rules. You can use this to stop background activity the physical system should perform while all instances are idle, for example with ``exec``.

onIdle
++++++

:⚡️ event:

This event is triggered when all Duo instances are idle according to their configured activity rules. This is the counterpart to ``onUsage`` and can be used to start performance-intensive background tasks.

onInstanceUsage
+++++++++++++++

:⚡️ event:
:inherited:

This event is triggered on every inspection cycle in which the instance reports activity.

onInstanceDemand
++++++++++++++++

:⚡️ event:
:inherited:

This event is triggered by an incoming request to an instance that is not running. You can configure ``start`` to start the instance. See :doc:`actions` for the available instance actions.

onInstanceIdle
++++++++++++++

:⚡️ event:
:inherited:

This event is triggered when a running instance becomes idle. You can configure ``stop`` or a delayed action such as ``stop+5min``. Renewed activity cancels a pending idle action.

onInstanceLogin
+++++++++++++++

:⚡️ event:
:inherited:

This event is triggered when the associated session logs in.

onInstanceStart
+++++++++++++++

:⚡️ event:
:inherited:

This event is triggered when the instance has started.

onInstanceStop
++++++++++++++

:⚡️ event:
:inherited:

This event is triggered when the instance has stopped and its associated session has ended.

onInstanceLogout
++++++++++++++++

:⚡️ event:
:inherited:

This event is triggered when the associated session logs out.

Instance
--------

If you need to configure individual instances differently from the monitor-level defaults, add an ``<Instance>`` child element identified by its name in the Duo Manager. Attributes on an ``<Instance>`` override the inherited defaults for that instance only; all other instances are unaffected. Matching ``<SessionMonitor>`` selectors also contribute to the associated session; instance settings are applied afterward.

.. code:: xml

    <Instance name="Thomas Anderson"
      watchStreamTraffic="true"
      watchInput="true"
      watchInputRemote="true"
      watchInputDisconnected="false"
      maxLastInputTime="10min"

      onUsage=""
      onDemand="start"
      onIdle="stop"

      onLogin=""
      onStart=""
      onStop=""
      onLogout=""
    />

name
++++

:required:

The logical name of the instance as configured in the Duo Manager.

.. important::

  This is the internal instance name, not the display name shown in the Duo Manager UI.

watchStreamTraffic
++++++++++++++++++

:default: *inherited from DuoSessionMonitor*

Makes the instance's streaming service available as the ``StreamTraffic`` metric.

minStreamTraffic
++++++++++++++++

:default: *inherited from minInstanceStreamTraffic*

The streaming-service traffic threshold for this instance. Requires packet capture and accepts the :doc:`network traffic formats </modules/network/attributes/traffic>`.

watchInput
++++++++++

:default: *inherited from SessionMonitor*

Enables the session's ``Input`` activity metric. Disabling input tracking removes this metric from the available watch-expression operands.

watchInputRemote
++++++++++++++++

:default: ``true``

Applies the input timeout to the remote session. When false, an attached remote client counts as input activity regardless of the time since the last input.

watchInputDisconnected
++++++++++++++++++++++

:default: *inherited from SessionMonitor*

Allows recent input to count after the session disconnects. Process activity remains available independently of this setting.

maxLastInputTime
++++++++++++++++

:⏱️ duration:
:default: *inherited from SessionMonitor*

The maximum age of session input that counts as activity. Uses the inspection interval when no explicit timeout is configured.

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

The cumulative per-process network traffic threshold for the session. Measures process transfers independently of the streaming-service threshold. See :doc:`/modules/process/attributes/traffic` for supported formats.

watch
+++++

:default: ``default OR``

Combines ``Input``, configured process metrics, and ``StreamTraffic`` in an activity expression. Referenced metrics must be enabled and have any required thresholds configured. By default, any supplied metric can count as activity.

Nested ``Process`` watches independently keep the instance active even if its own expression is false. See :doc:`/concepts/metrics` for metric details and :doc:`/concepts/metrics/watch` for expression syntax and composition.

onDemand
++++++++

:⚡️ event:
:inherited:

This event is triggered by an incoming request to an instance that is not running. You can configure ``start`` to start the instance. See :doc:`actions` for the available instance actions.

onUsage
+++++++

:⚡️ event:
:inherited:

This event is triggered on every inspection cycle in which the instance reports activity.

onIdle
++++++

:⚡️ event:
:inherited:

This event is triggered when a running instance becomes idle. You can configure ``stop`` or a delayed action such as ``stop+5min``. Renewed activity cancels a pending idle action.

onLogin
+++++++

:⚡️ event:
:inherited:

This event is triggered when the associated session logs in.

onStart
+++++++

:⚡️ event:
:inherited:

This event is triggered when the instance has started.

onStop
++++++

:⚡️ event:
:inherited:

This event is triggered when the instance has stopped and its associated session has ended.

onLogout
++++++++

:⚡️ event:
:inherited:

This event is triggered when the associated session logs out.

.. _duo-instance-host-filter-rule:

HostFilterRule
--------------

Filters the client addresses that contribute to an instance's network demand and streaming activity. Place the rule inside ``Instance``:

.. code:: xml

  <Instance name="Neo">
    <HostFilterRule IPv4="192.168.178.20" type="Must" />
  </Instance>

This example restricts network demand and streaming activity to the specified client. Multiple rules are supported. The attributes, named-host references, and ``Must``/``MustNot`` semantics are documented in the :ref:`NetworkMonitor HostFilterRule reference <network-host-filter-rule>`.

Instance filters require :doc:`packet capture <demand>`; configuring them in TCP listener mode is an error. They apply to this instance's Sunshine service, including its TCP and UDP ports. Input and process metrics are unaffected. These are monitoring filters; they do not prevent clients from connecting to an already running instance.

.. _duo-instance-host-range-filter-rule:

HostRangeFilterRule
-------------------

Filters client address ranges for the instance, with the same scope and packet-capture requirement as :ref:`duo-instance-host-filter-rule`:

.. code:: xml

  <Instance name="Neo">
    <HostRangeFilterRule network="192.168.178.0/24" type="Must" />
    <HostFilterRule IPv4="192.168.178.20" type="MustNot" />
  </Instance>

This example counts clients in the specified subnet, except for the excluded address. See the :ref:`NetworkMonitor HostRangeFilterRule reference <network-host-range-filter-rule>` for CIDR notation, address bounds, named-range references, and rule types.

Process
-------

Nested process watches use the :doc:`session process configuration
</modules/session/config>`. An active nested process keeps the instance active
independently of its own ``watch`` expression.
