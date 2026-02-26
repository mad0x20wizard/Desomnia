Configuration
=============

To enable the plugin, you have to add a ``<DuoStreamMonitor>`` to your cofiguration. This plugin reads the available instances from the Duo config, so you do not have to configure any particular instance at all. 

DuoStreamMonitor
----------------

If you need to set attributes for an individual instance, you can then add an ``<Instance>`` as a child node to the monitor, identified by their name in the Duo Manager. This does not affect any of the other instances. In order to react to an event for all available instances, use the appropriate ``onInstance...`` handler. Otherwise use the individual event handler on the instance.

.. attention::

  None of the events has a default value, so you have to be explicit here.

.. code:: xml

  <SystemMonitor>

    <DuoStreamMonitor serviceName="DuoService" refresh="5s"
      onInstanceDemand="start"
      onInstanceIdle="stop"
      
      onInstanceLogin=""
      onInstanceStarted=""
      onInstanceStopped=""
      onInstanceLogoff="">

      <Instance name="Neo" ... />
      <Instance name="Thomas Anderson" ... />

    </DuoStreamMonitor>

  </SystemMonitor>

serviceName
+++++++++++

:default: ``DuoService``

This attribute configures the name of the Duo service as it is displayed in the Service Control Manager (SCM). In order to monitor the lifecycle of the service this name has to match exactly.

refresh
+++++++

:⏱️ duration:
:default: ``5s``

This attribute configures the interval at which the state of the Duo instances is polled from the Duo Manager.

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

onInstanceLogoff
++++++++++++++++

:⚡️ event:
:inherited:

.. include:: attributes/logoff.rst

Instance
--------

.. code:: xml

    <Instance name="Thomas Anderson"
      onDemand="start"
      onIdle="stop"
      
      onLogin=""
      onStart=""
      onStop=""
      onLogoff=""
    />

name
++++

To correctly identify the instance, you have to provide it's logical name here. 

.. important::
  This is **not** the display name. It's the one you have set, when you created the Duo instance.

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

onLogoff
++++++++

:⚡️ event:

.. include:: attributes/logoff.rst
