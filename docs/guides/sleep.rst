Local Resource Management
=========================

:OS: 🪟 Windows

Where are the limits?
---------------------

Windows already has a feature that allows it to check if it is in an idle state and to suspend itself if it detects this. As a part of this, individual system components and user processes can request that the system should stay awake. You can check this 

.. code:: PowerShell

    powercfg /requests

::

    SYSTEM:
    [PROCESS] \Device\HarddiskVolume3\Program Files\VideoLAN\vlc.exe

    DISPLAY:
    None.

There user processes will show up with their location on your hard drive, so you could theoretically stop them or change something about their configuration, if you do not want your system to stay awake for this reason. But more often than not, kernel drivers use APIs to register such requests, that will provide no useful information about why this request was created in the first place.

Although you can instruct Windows to ignore requests from specific sources (with ``powercfg /requestsoverride``), this won't help if a driver uses one of the legacy APIs. And since these overrides don't survive a reboot, they need to be installed again after every restart.

As you can see, your control over this is very limited:

- Power requests cannot be filtered in a reliable way.
- Overrides have to be installed after every reboot.
- It is not possible to allow only specific requests and ignore the rest.

What lies beyond.
+++++++++++++++++

Desomnia attempts to overcome these limitations by suppressing the built-in behaviour first. You can then use a set of fine-grained rules and filters to customize your system's sleep behaviour exactly to your needs. In the following sections we will explore the metrics that can be monitored to determine whether the system is idle or not. You will also learn how individual system components can be stopped or started in response to changing demands.

Timing is everything
--------------------

.. code:: xml

    <?xml version="1.0" encoding="utf-8"?>
    <SystemMonitor version="1" timeout="2min">

        <!-- ... monitor configuration goes here ... -->

    </SystemMonitor>

To minimize the load on your system, Desomnia uses a time-based approach to check the monitored resources. Each time the configured ``timeout`` elapses, it asks every configured monitor wheather any of the watched resources are currently in use or have been used during the previous timeframe.

Without further configuration, only the used resources will be logged. In some cases, this may be all you need to know. If you want Desomnia to actively change the sleep behaviour, continue reading.

Monitoring resources
--------------------

Desomnia organizes your system into a tree structure of logical resources. The root-level resource is the ``<SystemMonitor>``, representing the system as a whole, and this will be divided into monitors and resources with a decreasing scope. Each resource has a state that determines whether it is idle and can trigger one or more events, which enable you to manipulate their lifecycle.

Common events
-------------

You can recognize the attributes, that define how Desomnia should respond to different events, by their ``on`` prefix. This is followed by the name of the event and an action, configured by the value of the attribute.

Some events allow you to delay the execution of an action, which can be useful if you don't want a state change to trigger an immediate response.

onIdle
++++++

:⚡️ event:

Most resources allow you to configure the ``onIdle`` action, which always triggers during the *timeout phase*. The available actions vary depending on the type of resource and the contextual monitor.

For example: In order to control the sleep behaviour of your system, you may use the ``sleep`` action for the ``onIdle`` event on the ``<SystemMonitor>``.

.. code:: xml

    <?xml version="1.0" encoding="utf-8"?>
    <SystemMonitor version="1" timeout="2min" onIdle="sleep+10min">

        <!-- ... -->

    </SystemMonitor>

This will instruct Desomnia to suspend your system as soon as no resources have been used for the configured timeout period.

.. note::

    We also configured to delay the ``sleep`` action for **10 minutes**. The action will only be executed if the system remains in the idle state for that length of time; otherwise, it will be cancelled. If it then switches to the idle state again, the delay starts again from the beginning.

onDemand
++++++++

:⚡️ event:

Many resources also allow you to specify an action for the ``onDemand`` event. This action will be executed by no later than the next timeout check, but some resource monitors will occasionally support to take immediate action. 

The ``<SystemMonitor>`` triggers this event, when it detects, that some resource in the system is in use. The most idiomatic action, that you can configure here, would be ``sleepless``, which instructs Desomnia to create a power request, to prevent the system from suspending itself in accordance with the built-in power management settings. The power request will be released, when the system switches to the idle state again.

.. code:: xml

    <?xml version="1.0" encoding="utf-8"?>
    <SystemMonitor version="1" timeout="2min" onIdle="sleep" onDemand="sleepless">

        <!-- ... -->

    </SystemMonitor>

By specifying both attributes, you can effectively bypass the built-in behaviour. This is the recommended configuration when using Desomnia as a full replacement for OS power management.

Start from here
---------------

If you are new to this, it would be advisable to familiarize yourself with how to configure Desomnia to mimic the default behaviour of the built-in power management system, and then customize it to your individual needs from there:

.. code:: xml

    <?xml version="1.0" encoding="utf-8"?>
    <SystemMonitor version="1" timeout="2min" onIdle="sleep" onDemand="sleepless">

        <SessionMonitor />
        <NetworkSessionMonitor />
        <PowerRequestMonitor />

    </SystemMonitor>

Windows' built-in power management system monitors these sources by default, so configuring Desomnia like this should make your system behave roughly the same as before. However, unlike with the built-in system, you can now add various rules and filters to accommodate even very specific use cases. Desomnia also includes additonal sources, that can extend your control ofter the sleep cycle of your system even further.

Exploring the core modules
--------------------------

The following monitors can be used without the need to install additional plugins. Each of these has it's own chapter in the documentation, so we will only briefly describe their purpose and how they relate to the built-in power management:

``<SystemMonitor>``
+++++++++++++++++++

This monitor acts as a meta-monitor and container for the actual usage monitors. It provides events and actions that enable you to customise your system’s sleep behaviour. Read more about the :doc:`/modules/system/monitor` to understand its role in Desomnia’s basic architecture.

``<SessionMonitor>``
++++++++++++++++++++

Usually the system will be be considered non-idle and stay awake for as long as an actual user is interacting with the computer. Windows uses various indicators to determine this, such as mouse and keyboard activity.

If you activate this monitor, it will track the activity of user sessions and their idle time will contribute to the system's overall idle state. Read more about how to configure the :doc:`/modules/session/monitor` to constrain this behaviour to individual user accounts, and how to use session events. For example, you can set it up to automatically log out idle sessions or to stop idle processes.

``<NetworkSessionMonitor>``
+++++++++++++++++++++++++++

You can configure this monitor if you want the system to stay awake while someone uses its shared files and folders remotely. Any network session will be considered as non-idle. If you want more control over which access should be considered non-idle, read more about how to configure the :doc:`/modules/network_session/monitor` to filter the connected sessions.

``<PowerRequestMonitor>``
+++++++++++++++++++++++++

Any process or system driver can request the system to stay awake. If you configure this monitor, active power requests will render the system as non-idle. Read more about how to configure the :doc:`/modules/power/monitor` to add filters that either exclude requests or allow specific ones only.

``<ProcessMonitor>``
++++++++++++++++++++

The built-in power management system does not provide any means to keep the system awake by the mere presence of a process. You have to read about how to configure the :doc:`/modules/process/monitor` in order to monitor individual processes or whole groups of them and set CPU thresholds by which they should keep the system awake. You can use this monitor, to give any process the ability to issue power requests, even if they originally were not intended to do so.

``<NetworkMonitor>``
++++++++++++++++++++

The features of this monitor go beyond basic idle checks and enable you to orchestrate entire network infrastructures. It achieves this by using packet-capturing techniques to dynamically detect when local or remote services are accessed, enabling you to switch their hosts on and off as required. There is a dedicated guide explaining how to do :doc:`wake`. Alternatively you can learn more about how to configure the :doc:`/modules/network/monitor` in order to watch local network services and prevent the system from going to sleep while they are in use.