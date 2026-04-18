Wake-on-LAN: Client
========================

:OS: 🪐 *Platform-independent*

In client mode, Desomnia runs on **the machine you use to connect** to other hosts — your laptop, desktop, or workstation. It monitors your outgoing network traffic and automatically sends a Magic Packet to wake a sleeping host the moment you try to connect to one of its services.

This mode requires no dedicated device. If you have a machine in your network that runs around the clock — such as a Raspberry Pi or a NAS — consider the :doc:`wol-proxy` guide instead, which provides Wake-on-LAN coverage for your entire network from a single installation.

.. note::
   The configuration for both modes is nearly identical. The only difference is where you install and run Desomnia. If you later decide to switch to proxy mode, your existing configuration carries over with minimal changes.

Before you begin
----------------

Make sure the following are in place before writing your first configuration:

- **Desomnia is installed** on the machine you connect *from*. Refer to the Installation section of this documentation for your platform.
- **Wake-on-LAN is enabled** on the target host. This typically requires enabling it in the BIOS / UEFI firmware of the target machine and ensuring that the network adapter remains powered while the system is suspended. The exact setting is often labelled *Wake-on-LAN*, *Wake on Magic Packet*, or *Power On By PCI-E*, depending on the firmware vendor.
- You have the **MAC address** of the target host. Magic Packets are addressed by MAC, not by IP, so this is always required. If the host is currently awake, run ``arp -a`` from any machine on the same network, or look it up in your router's device list.

.. include:: _wol/wakeonlan.rst

Minimum working configuration
------------------------------

The following configuration tells Desomnia to watch your network and send a Magic Packet to ``"server"`` whenever an outgoing connection to it is detected:

.. code:: xml

   <?xml version="1.0" encoding="UTF-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor>
       <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10" />
     </NetworkMonitor>

   </SystemMonitor>

Replace ``00:1A:2B:3C:4D:5E`` with the MAC address of your target host and ``192.168.1.10`` with its IP address.

The ``<NetworkMonitor>`` element here specifies neither an ``interface`` nor a ``network`` attribute, which tells Desomnia to automatically bind to all interfaces that have a default gateway configured — normally just the interface connected to your local network. If you have multiple active network connections and need to target a specific one, see :doc:`/modules/network/interface`.

The ``IPv4`` attribute on ``<RemoteHost>`` is optional in many environments. If your router provides DNS for local hosts, Desomnia can resolve the address automatically — see :doc:`/modules/network/auto` to learn how to remove static address mappings from your configuration once you have a working baseline.

.. note::
   With this configuration, Desomnia reacts to **any** outgoing connection directed at ``"server"`` — including background traffic from your operating system or other applications. The :ref:`filtering section <filtering-unwanted-wakeups>` below explains how to restrict this to only the services you use.

Verifying it works
------------------

Start the Desomnia service, then try connecting to a service on the target host while it is suspended. Desomnia logs a message each time it detects a connection attempt and each time it sends a Magic Packet.

To see the full detail of what is being detected, enable debug-level logging — see :doc:`/concepts/logging`.

If the host does not wake up, check the following:

- Wake-on-LAN is enabled in the BIOS / UEFI settings of the target host.
- The network adapter on the target host is configured to remain powered while suspended.
- The MAC address and IP address in the configuration match the target host exactly.
- Desomnia is monitoring the correct network interface — confirm with :doc:`/modules/network/interface`.

.. _filtering-unwanted-wakeups:

Filtering unwanted wake-ups
----------------------------

.. include:: _wol/service-filtering.rst
