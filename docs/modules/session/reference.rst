Configuration reference
=======================

SessionMonitor
--------------

You can configure any number of selectors to watch OS user sessions. If multiple selectors target the same session — for example, if you configure actions that target all users and all administrators — the watched session will include the configured actions for both groups.

.. code:: xml

  <SystemMonitor>

    <SessionMonitor
      clockRemote="false"
      clockDisconnected="false">

      <User ... />
      <Administrator ... />
      <Group ... />

      <Everyone ... />

    </SessionMonitor>

  </SystemMonitor>

.. include:: options/clock.rst

User
----

All users, identified by their name, will be matched by this selector.

.. code:: xml

  <User name="Smith" 

    clockRemote="false"
    clockDisconnected="false"

    maxIdleTime="20min"

    onIdle="logout"
    onLogin=""
    onRemoteLogin=""
    onConsoleLogin=""
    onDisconnect=""
    onLogout="">
    
    <Process ... />
  
  </User>

name
++++

:🔍 regex:

The value of this attribute is interpreted as a regular expression which is compared with the actual user name of the session. Only matching sessions will be watched with the configured actions.

maxIdleTime
+++++++++++

:⏱️ duration:

A session is usually considered idle when no input has been detected for longer than the global timeout period. However, since the operating system remembers the time of the last input independently of Desomnia, you can specify a different time period after which the session should be considered idle.

.. note::

  If multiple selectors configure the ``maxIdleTime`` option for the same session, the longest duration specified will be used.

.. include:: options/clock.rst

onIdle
++++++

:⚡️ event:

This event is triggered if the processing time used since the last timeout is less than the configured ``minCPU`` value. If no CPU threshold is configured, this event will not be triggered.

onLogin
+++++++

:⚡️ event:

This event is triggered when a user logs in.

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

This event is triggered when the watched session is connected to the console session, after it was disconnected.

onRemoteConnect
+++++++++++++++

:⚡️ event:

This event is triggered when the watched session is connected to a remote client, after it was disconnected.

onDisconnect
++++++++++++

:⚡️ event:

This event is triggered when the user disconnects from the watched session. 

onLogout
++++++++

:⚡️ event:

This event is triggered when the user logouts of the watched session. 

Process
-------

The configuration of ``<Process>`` groups is the same as described in :doc:`/modules/process/monitor`, expect that these processes will only be considered in the context of the specified session and have some additional events to configure:

.. code:: xml

  <Process name="VLC Media Player" ...

    onSessionIdle="stop"
    onSessionConsoleConnect=""
    onSessionRemoteConnect=""
    onSessionDisconnect="">

    vlc

  </Process>

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

Only users with administrative permissions will be matched by this selector.

Apart from that everything is configured exactly the same as ``<User>``.

Group
-----

Only users that belong to the specified group, identified by it's name, will be matched by this selector.

.. admonition:: Work in progress

  This still has to be implemented.

Everyone
--------

All users will be matched by this selector, so you cannot configure the ``name`` attribute here.