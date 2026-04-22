Router configuration
====================

The ``<Router>`` element tells Desomnia which device on the network is the default gateway. Declaring the router enables controls over which packets originating from or forwarded by that device are permitted to trigger wake-up requests.

By default, Desomnia filters out all packets whose physical sender is the router, to prevent routine background traffic — periodic reachability checks, address resolution from the router itself — from waking sleeping hosts unintentionally. The attributes below selectively relax this restriction.

allowWake
---------

:default: ``false``

Setting ``allowWake="true"`` permits any packet originating from the router to trigger a wake-up, without restriction. This effectively removes the router filter entirely for that device. Use this only if the router itself is the intended wake-up trigger, or if you are certain no unwanted traffic reaches it from outside.

allowWakeByProxy
----------------

:default: ``false``

Setting ``allowWakeByProxy="true"`` permits packets **forwarded** by the router — those carrying a source IP different from the router's own — to trigger wake-ups. The router itself still cannot initiate a wake-up on its own.

This is the appropriate setting for VPN deployments where the router acts as a VPN gateway: the VPN client's source IP is preserved in the forwarded packet, and Desomnia can match it against configured hosts and filters.

VPN client presence detection
------------------------------

Rather than enabling ``allowWakeByProxy`` unconditionally, you can declare known VPN clients as ``<VPNClient>`` entries. Desomnia then checks their presence via ICMP ping and enables proxy wake-up dynamically:

- If **at least one** ``<VPNClient>`` responds within the configured ``vpnTimeout``, forwarded packets from the router are permitted to trigger wake-ups.
- If **none** respond, forwarded packets are blocked — preventing idle internet traffic from reaching sleeping hosts when no VPN client is connected.

.. code:: xml

   <Router name="gateway" hostName="fritz.box" IPv4="192.168.178.1" MAC="b0:f2:08:0a:d1:14" vpnTimeout="250ms">
     <VPNClient name="Alice" IPv4="192.168.178.201" />
     <VPNClient name="Bob" IPv4="192.168.178.202" />
   </Router>

The ``vpnTimeout`` attribute specifies how long to wait for a ping response. On a local network, responses arrive in single-digit milliseconds; 250–500 ms is generally sufficient.

.. admonition:: Work in progress

   The ``vpnFrequency`` attribute (documented in the :doc:`configuration reference <config>`) is not yet implemented — periodic re-checking of VPN client presence has no effect in the current version. Use ``vpnTimeout`` exclusively for now.

.. note::

   Some routers answer address resolution requests for VPN client IPs even when the client is not connected. ICMP ping is the only reliable way to confirm actual presence in such cases.

Known routers
-------------

The following pages document specific router models and their compatibility with Desomnia's Wake-on-LAN and VPN features:

.. list-table::
   :header-rows: 1
   :widths: 25 75

   * - Router
     - Summary
   * - :doc:`FRITZ!Box <routers/fritzbox>`
     - | Built-in IPsec and WireGuard VPN; proxy ARP for VPN clients
       | ARP answered for VPN clients regardless of connection state — use ``<VPNClient>`` with ping
       | No static ARP entries — remote unicast WoL not supported; use promiscuous proxy instead

.. admonition:: Help expand this list

   If you have confirmed the behaviour of a router model not listed here, please open an issue or pull request on the `GitHub repository <https://github.com/mad0x20wizard/Desomnia>`_ with the details.

.. toctree::
   :maxdepth: 1

   routers/fritzbox
