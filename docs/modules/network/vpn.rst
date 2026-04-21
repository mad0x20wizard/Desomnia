VPN support
===========

Connecting to a sleeping host from outside the local network requires a VPN to route the connection attempt into the internal network. Whether Desomnia can identify individual VPN clients and apply per-client filters depends on how the VPN presents traffic on the local segment.

How Magic Packets travel
------------------------

A Magic Packet is normally sent as a Layer 2 Ethernet broadcast, which means it cannot be routed across networks. To reach a sleeping host from outside, one of two approaches is needed:

.. _vpn-proxy:

- A device **inside** the network receives the connection attempt and sends the Magic Packet locally on the remote client's behalf. The most practical way to achieve this is to run Desomnia in :doc:`promiscuous mode <promiscuous>` on an always-on device. The VPN delivers the connection attempt into the local network; Desomnia sees it, sends the Magic Packet, and claims the sleeping host's IP to buffer the incoming connection until the host wakes up. No configuration is needed on the connecting device or the router.

.. _vpn-unicast:

- The Magic Packet is sent as a **Layer 3 UDP packet** addressed directly to the sleeping host's IP, which can be routed across networks. This requires the router to have a static IP-to-MAC address mapping for the sleeping host. Not all consumer routers support static ARP entries — see the router pages below.

VPN network models
------------------

From Desomnia's perspective, the key question is whether the VPN client's original source IP address arrives intact at the local network. This determines whether Desomnia can identify individual clients and apply filters.

Layer 2 VPN (bridged)
+++++++++++++++++++++

The VPN client appears on the local network with its own MAC address and a local IP, as if physically connected. Desomnia sees it like any other local device — no special configuration needed.

This is the most transparent model but uncommon in consumer deployments.

Layer 3 VPN with proxy ARP
++++++++++++++++++++++++++

The most common model in consumer and small-office setups. VPN clients receive IP addresses, but the VPN gateway intercepts address resolution queries on their behalf and answers with its own MAC address. Desomnia sees the VPN client's source IP, but the gateway's MAC as the physical sender. Individual clients are identifiable by IP.

If the VPN gateway is the default router, declare it as a ``<Router>`` and use ``<VPNClient>`` entries for presence detection. See :doc:`router` for details.

Layer 3 VPN with masquerading
++++++++++++++++++++++++++++++

The VPN gateway replaces each client's source IP with its own before forwarding. Desomnia cannot distinguish individual VPN clients — all VPN traffic appears to originate from the gateway. Wake-on-LAN still works; per-client filtering is not possible.

VPN solutions
-------------

.. list-table::
   :header-rows: 1
   :widths: 25 75

   * - Solution
     - Summary
   * - :doc:`Tailscale <vpn/tailscale>`
     - | Default: masquerading — all traffic appears as the local Tailscale node's IP
       | With ``--snat-subnet-routes=false``: source IPs preserved (tailnet range ``100.64.0.0/10``)
   * - :doc:`WireGuard <vpn/wireguard>`
     - | Layer 3; source IPs preserved when deployed with subnet routing and proxy ARP
       | If deployed on a router, see the relevant router page
   * - :doc:`OpenVPN <vpn/openvpn>`
     - | ``tap`` mode: Layer 2 bridged — clients appear as local devices, fully transparent
       | ``tun`` mode: Layer 3 — source IPs visible if proxy ARP or subnet routing is used
   * - FRITZ!Box built-in VPN
     - | Proxy ARP; VPN clients assigned IPs in the local subnet
       | See :ref:`FRITZ!Box VPN <fritzbox-vpn>`

.. toctree::
   :maxdepth: 1

   vpn/tailscale
   vpn/wireguard
   vpn/openvpn
