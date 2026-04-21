WireGuard
=========

WireGuard is a lightweight VPN protocol. It can be deployed as a standalone server on a dedicated device (such as a Raspberry Pi or a cloud VM) or as part of a router with built-in WireGuard support.

Network model
-------------

WireGuard operates at Layer 3. When the WireGuard server is configured with subnet routing and proxy ARP, VPN clients' source IPs arrive intact at the local network. The WireGuard host answers address resolution on behalf of remote clients, so Desomnia sees the client's source IP with the WireGuard host's MAC as the physical sender. Individual clients are identifiable by IP, and per-client filtering is possible.

Router-integrated WireGuard
++++++++++++++++++++++++++++

If your router has built-in WireGuard support, refer to the relevant router page:

- :doc:`../routers/fritzbox` — FRITZ!Box (FRITZ!OS 7.50 and later)

Standalone WireGuard server
++++++++++++++++++++++++++++

A standalone WireGuard server on a dedicated device is not the default router, so no ``<Router>`` declaration is needed. Packets from VPN clients with preserved source IPs arrive at Desomnia like any other local traffic and are handled normally.

If your configuration includes a ``<ForeignHostFilterRule>`` and VPN clients are assigned IPs outside the local subnet, declare the VPN subnet as a ``<HostRange>`` to allow their traffic through. See :doc:`tailscale` for an example of this pattern — the approach is identical.

If you want the same VPN client presence detection behaviour described in :doc:`../router`, you can optionally declare the WireGuard host as a ``<Router>`` and configure ``<VPNClient>`` entries. This is not required unless you want Desomnia to actively verify client connectivity before permitting forwarded requests to trigger wake-ups.
