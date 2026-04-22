Configuration
=============

To enable the plugin, add a ``<DuoStreamMonitor>`` to your configuration. The plugin reads the available instances from the Duo Manager, so no individual instance configuration is required to get started.

DuoStreamMonitor
----------------

.. code:: xml

  <SystemMonitor>

    <DuoStreamMonitor serviceName="DuoService" refresh="5s"
      onDemand="sleepless"
      onIdle=""

      onInstanceDemand="start"
      onInstanceIdle="stop"

      onInstanceLogin=""
      onInstanceStarted=""
      onInstanceStopped=""
      onInstanceLogout="">

      <Instance name="Neo" ... />
      <Instance name="Thomas Anderson" ... />

    </DuoStreamMonitor>

  </SystemMonitor>

.. attention::

  None of the events has a default value, so you have to be explicit here.

serviceName
+++++++++++

:default: ``DuoService``

The name of the Duo service as it appears in the Windows Service Control Manager (SCM). This must match exactly for Desomnia to track the service lifecycle.

refresh
+++++++

:⏱️ duration:
:default: ``5s``

The interval at which the state of the Duo instances is polled from the Duo Manager web interface. Instance start and stop events are currently detected by polling; a future version will use Windows Event Log notifications instead.

onDemand
++++++++

:⚡️ event:

Triggered when any Duo instance receives a connection request and at least one instance transitions from idle to active — that is, when the monitor as a whole goes from fully idle to having at least one running session. You can use this to stop any background activity, the physical system should perform while no streaming client is connected, for example with ``exec``.

onIdle
++++++

:⚡️ event:

Triggered during the timeout phase when no Duo instance has an active client connection — that is, when all instances have become idle. This is the counterpart to ``onDemand`` and can be used to startup performance intense background tasks.

onInstanceDemand
++++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/demand.rst

onInstanceIdle
++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/idle.rst

onInstanceLogin
+++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/login.rst

onInstanceStart
+++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/start.rst

onInstanceStop
++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/stop.rst

onInstanceLogout
++++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/logout.rst

Instance
--------

If you need to configure individual instances differently from the monitor-level defaults, add an ``<Instance>`` child element identified by its name in the Duo Manager. Attributes on an ``<Instance>`` override the inherited defaults for that instance only; all other instances are unaffected.

.. code:: xml

    <Instance name="Thomas Anderson"
      onDemand="start"
      onIdle="stop"

      onLogin=""
      onStart=""
      onStop=""
      onLogout=""
    />

name
++++

The logical name of the instance as configured in the Duo Manager.

.. important::

  This is the internal instance name, not the display name shown in the Duo Manager UI.

onDemand
++++++++

:⚡️ event:

.. include:: attributes/demand.rst

onIdle
++++++

:⚡️ event:

.. include:: attributes/idle.rst

onLogin
+++++++

:⚡️ event:

.. include:: attributes/login.rst

onStart
+++++++

:⚡️ event:

.. include:: attributes/start.rst

onStop
++++++

:⚡️ event:

.. include:: attributes/stop.rst

onLogout
++++++++

:⚡️ event:

.. include:: attributes/logout.rst
