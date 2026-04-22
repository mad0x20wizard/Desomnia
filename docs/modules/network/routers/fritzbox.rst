FRITZ!Box
=========

The FRITZ!Box is a common consumer router/modem with built-in VPN support. It is the most widely tested router model with Desomnia.

Network model
-------------

The FRITZ!Box uses **Layer 3 VPN with proxy ARP**. VPN clients receive IP addresses from the internal subnet and never appear on the local network with their own MAC address. Instead, the FRITZ!Box intercepts address resolution queries on behalf of connected VPN clients and answers with its own MAC address. Local hosts — and Desomnia — therefore see the VPN client's source IP, but the router's MAC as the physical sender.

.. _fritzbox-vpn:

VPN
+++

The FRITZ!Box supports two built-in VPN protocols:

- **IPsec (FRITZ!VPN)** — the legacy VPN protocol; clients receive IPs from the internal subnet
- **WireGuard** — available from FRITZ!OS 7.50; also assigns clients IPs from the internal subnet

In both cases, the VPN client's source IP is preserved when forwarded into the local network, and Desomnia can identify individual clients.

.. warning::

   The FRITZ!Box answers ARP requests for VPN client IPs even when the client is **not** connected. Desomnia cannot rely on address resolution alone to determine whether a VPN client is actually present. Declare VPN clients as ``<VPNClient>`` entries and use ICMP ping for reliable presence detection.

If you do not use ``allowWake="true"`` or ``allowWakeByProxy="true"``, declare the FRITZ!Box as a ``<Router>`` and add a ``<VPNClient>`` entry for each known VPN client:

.. code:: xml

   <Router name="fritz.box" MAC="b0:f2:08:0a:d1:14" IPv4="192.168.178.1" vpnTimeout="250ms">
     <VPNClient name="Alice" IPv4="192.168.178.201" />
     <VPNClient name="Bob" IPv4="192.168.178.202" />
   </Router>

Desomnia pings each declared client before permitting the router to forward wake-up triggers. When at least one responds within ``vpnTimeout``, forwarded packets are allowed through. When none respond, they are blocked. See :doc:`../router` for a full description of ``<Router>`` attributes.

Wake-on-LAN from outside
+++++++++++++++++++++++++

The FRITZ!Box does not support static IP-to-MAC address mappings, so the :ref:`unicast WoL approach <vpn-unicast>` is not available. The recommended alternative is to run Desomnia in :doc:`promiscuous mode <../promiscuous>` on an always-on device inside the network. The VPN delivers the connection attempt into the local network; Desomnia sees it, sends the Magic Packet, and claims the sleeping host's IP to buffer the connection until the host wakes up. No static router configuration is required.

Auto discovery
--------------

🚧 This section describes upcoming features that are not yet available in the current release.

A future version of Desomnia will include a FRITZ!Box integration that queries the router's ARP and DHCP tables directly, enabling:

- **Automatic MAC address discovery**: MAC addresses for known hosts will be resolved from the router without manual configuration, including for hosts that are currently offline.
- **VPN client discovery**: connected VPN clients will be detected automatically, removing the need to declare ``<VPNClient>`` entries individually.

These features will make FRITZ!Box-based configurations significantly less verbose and will not require any changes on the router itself.

Capability summary
------------------

.. list-table::
   :header-rows: 1
   :widths: 45 15 40

   * - Feature
     - Support
     - Notes
   * - VPN client source IPs visible
     - ✅ Yes
     - Proxy ARP; clients assigned IPs in the local subnet
   * - Reliable VPN client presence detection
     - ✅ Yes (with ping)
     - ARP alone is unreliable — always use ``<VPNClient>`` with ``vpnTimeout``
   * - Static ARP / MAC mapping
     - ❌ No
     - Unicast WoL not supported; use promiscuous proxy instead
