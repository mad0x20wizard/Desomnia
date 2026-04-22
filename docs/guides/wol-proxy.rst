Wake-on-LAN: Proxy
=======================

:OS: 🪐 *Platform-independent*

In proxy mode, Desomnia runs on an **always-on device** — such as a Raspberry Pi, a NAS, or any other machine that remains powered around the clock. From there, it monitors traffic across the entire network and wakes sleeping hosts on behalf of any device that tries to reach them, without requiring any software or configuration changes on the connecting devices.

If you do not have a suitable always-on device, the :doc:`wol-client` guide describes an alternative that achieves the same result by running Desomnia on each machine you connect from.

.. hint::
   The configuration for both modes is nearly identical. The only structural difference is a single attribute on the ``<NetworkMonitor>`` element. If you have already worked through the client mode guide, everything you have learned applies here as well.

.. attention::

   Only one Desomnia instance may run in proxy mode on a given local subnet at a time. On startup, Desomnia broadcasts a beacon to detect any running proxy instance. If another instance in proxy mode is present, it responds, and the secondary instance exits with an error that includes the MAC address of the conflicting instance.

   Two Desomnia instances operating on different subnets, or where only one is in proxy mode, are not affected by this restriction.

   .. admonition:: Work in progress

      Running two proxy instances for redundancy requires a more involved coordination mechanism — both instances would continuously respond to each other's address resolution queries, making each appear permanently online to the other. This is tracked for a future release.

Before you begin
----------------

Make sure the following are in place before writing your first configuration:

- **Desomnia is installed** on the always-on device — not on the machines you connect from. Refer to the Installation section of this documentation for the appropriate platform.
- The always-on device must remain **continuously online**. Combining proxy mode with local sleep management on the same machine is not recommended, as suspending the proxy would prevent it from waking other hosts.
- **Wake-on-LAN is enabled** on each target host. This requires enabling it in the BIOS / UEFI firmware and ensuring the network adapter remains powered while the system is suspended. The exact setting is often labelled *Wake-on-LAN*, *Wake on Magic Packet*, or *Power On By PCI-E*, depending on the firmware vendor.
- You have the **MAC address** of each target host. If a host is currently awake, run ``arp -a`` from any machine on the same network, or look it up in your router's device list.
- The always-on device must be on the **same network segment** as the hosts it will wake. Wake-on-LAN Magic Packets do not cross routers by default.

.. include:: _wol/wakeonlan.rst

Minimum working configuration
------------------------------

The configuration for proxy mode is identical to client mode, with one addition: ``watchMode="promiscuous"`` on the ``<NetworkMonitor>`` element.

.. code:: xml

   <?xml version="1.0" encoding="UTF-8"?>
   <SystemMonitor version="1">

     <NetworkMonitor watchMode="promiscuous">
       <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10" />
     </NetworkMonitor>

   </SystemMonitor>

Replace ``00:1A:2B:3C:4D:5E`` with the MAC address of your target host and ``192.168.1.10`` with its IP address.

Setting ``watchMode="promiscuous"`` switches Desomnia from monitoring only outgoing traffic of the local machine to monitoring connection attempts between *any* two hosts on the network. When a client tries to reach a sleeping host, Desomnia detects the attempt and sends the Magic Packet on its behalf. Read more about how this works in :doc:`/modules/network/promiscuous`.

As in client mode, the ``<NetworkMonitor>`` element without an ``interface`` or ``network`` attribute automatically binds to all interfaces with a default gateway configured. See :doc:`/modules/network/interface` if you need to target a specific interface, and :doc:`/modules/network/auto` to learn how to remove static address mappings from your configuration once you have a working baseline.

.. note::
   In promiscuous mode, Desomnia observes traffic from every device on the network. Without any filters, it will react to **any** connection attempt directed at ``"server"`` — including traffic from your router, smart home devices, or the proxy device itself. The sections below explain how to bring this under control.

Verifying it works
------------------

Start the Desomnia service on the always-on device, then try connecting to a service on a target host from any machine on the network while it is suspended. Desomnia logs a message each time it detects a connection attempt and each time it sends a Magic Packet.

To see the full detail of what is being detected, enable debug-level logging — see :doc:`/concepts/logging`.

If the host does not wake up, check the following:

- Wake-on-LAN is enabled in the BIOS / UEFI settings of the target host.
- The network adapter on the target host is configured to remain powered while suspended.
- The MAC address and IP address in the configuration match the target host exactly.
- The always-on device is on the same network segment as the target host.
- Desomnia is monitoring the correct network interface — confirm with :doc:`/modules/network/interface`.

Filtering unwanted wake-ups
----------------------------

.. include:: _wol/service-filtering.rst

.. caution::
   When using a ``<PingFilterRule>`` in promiscuous mode, Desomnia needs to :ref:`spoof the addresses of watched hosts <network-monitor-spoofing>` in order to intercept and inspect ping requests. If you have already configured ``type="Must"`` service filters (or ``<Service>`` declarations), a ``<PingFilterRule>`` is not needed — ping traffic is excluded automatically.

.. include:: _wol/host-filtering.rst


Combining service and host filters
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Service filters and host filters can be nested to express more precise conditions. The ``type`` of a nested rule is always interpreted relative to its parent. The following example demonstrates the possible combinations:

.. code:: xml

   <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">

     <ServiceFilterRule name="SSH" port="22" type="Must">
       <HostFilterRule name="workstation" IPv4="192.168.1.20" type="Must" />
     </ServiceFilterRule>

     <ServiceFilterRule name="RDP" port="3389" type="Must">
       <HostFilterRule name="proxy" IPv4="192.168.1.2" type="MustNot" />
     </ServiceFilterRule>

     <ServiceFilterRule name="SMB" port="445" type="MustNot">
       <HostFilterRule name="backup-agent" IPv4="192.168.1.50" type="Must" />
     </ServiceFilterRule>

     <PingFilterRule type="MustNot">
       <HostFilterRule name="monitor" IPv4="192.168.1.99" type="MustNot" />
     </PingFilterRule>

   </RemoteHost>

``<ServiceFilterRule>`` = **Must**    ×  ``<HostFilterRule>`` = **Must**
  Only wake for connections to this service, and only when they originate from the specified host.

``<ServiceFilterRule>`` = **Must**    ×  ``<HostFilterRule>`` = **MustNot**
  Only wake for connections to this service, but never when they originate from the specified host.

``<ServiceFilterRule>`` = **MustNot** ×  ``<HostFilterRule>`` = **Must**
  Ignore connections to this service in general, but only when they originate from the specified host. Connections from all other hosts are still allowed.

``<PingFilterRule>`` = **MustNot**    ×  ``<HostFilterRule>`` = **MustNot**
  Ignore ping traffic in general, but not when it originates from the specified host — that host's pings will still be allowed through.
