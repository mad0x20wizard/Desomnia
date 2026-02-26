Hyper-V Support
===============

:OS: 🪟 *Windows-only*

To use this plugin, you must first enable the `Hyper-V platform`_. It will then allow you to use virtual machines in bridged network mode with Desomnia. The following features are currently supported:

- Automatic MAC address detection of the virtual machines
- Start, suspend and stop virtual machines, based on actual network usage
- Start virtual machines on Wake-on-LAN (Magic Packet) directed at their MAC address
- Respond to ARP / NDP requests on behalf of sleeping machines
- Keep the physical system awake, while network services of virtual machines are used

.. attention::

  Although MAC addresses can be detected automatically, the plugin cannot detect the IP address of a virtual machine that is switched off, because the address is released when this happens. To query this information, you need another authority for IP address reservation, such as a router with DNS support.

Network interface selection
---------------------------

Using Hyper-V in bridged mode creates a virtual switch that is connected to a specific physical network adapter. You have to bind the NetworkMonitor to the virtual interface, because it will be the one carrying the actual IP configuration. This happens automatically when you :doc:`select the interface </module/network/interface>` by network or have it selected by presence of a standard gateway. 

On startup, if the plugin detects that a virutal adpater is selected, it will query to which physical adapter the switch is connected and use this to capture packets instead. This is necessary to capture all the traffic for each virtual machine, rather than just the packets directed at the physical host.

Example configuration
---------------------

Imagine using a self-hosted GitLab instance for your code repository and CI platform, running inside a virtual machine. This system uses a lot of resources, but you only need it for a few minutes each day. It would be wasteful to run it continually, but it is inconvenient to start and stop the machine every time you use it.

.. code:: xml

  <SystemMonitor version="1" timeout="2min" onIdle="sleep+1h" onDemand="sleepless">

    <NetworkMonitor network="192.168.178.0/24" autoDetect="MAC|IPv4|IPv6" watchYield="true">

      <Service name="SSH" port="22" />

      <VirtualHost name="gitlab" onIdle="suspend+10min">
        <Service name="SSH" port="22" />

        <HTTPService />
      </VirtualHost>

    </NetworkMonitor>

  </SystemMonitor>

With this configuration your GitLab instance will behave like any remote host, configured for Desomnia. When any client (including the local host) tries to access on of its configured services (SSH and HTTP), Desomnia will autostart the virtual machine. It will stay in the running state, as long as one of its services is used. The physical system will not suspend during that time either.

The network activity will be checked every 2 minutes. If there has not been any network traffic for that period, the VM will be suspended (have its memory written to disk), after another delay of 10 minutes. If network activity is detected during this time, the virtual machine is considered active again and the timer is cancelled.

Desomnia will eventually suspend the physical machine if no network activity is registered for an hour to either the virtual or the physical host. 

.. note::

  Before doing that, it will yield the responsibility for watching the configured local network interfaces (including the virtual interfaces of virtual machines). This allows another instance of Desomnia on the network running in :doc:`/modules/network/promiscuous` or a generic Sleep Proxy to pick up the watch immediately and continue to advertise the configured services.

.. _`Hyper-V platform`: https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/install-hyper-v