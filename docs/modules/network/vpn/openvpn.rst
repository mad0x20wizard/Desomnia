OpenVPN
=======

OpenVPN supports two fundamentally different operating modes — ``tap`` and ``tun`` — which have different network models and different implications for Desomnia.

Network model
-------------

tap mode (Layer 2 bridged)
++++++++++++++++++++++++++

In ``tap`` mode, OpenVPN creates a bridged network interface. VPN clients appear on the local network with their own MAC addresses and local IP addresses, as if physically connected. Desomnia sees them like any other local device — no special configuration needed.

This is the most transparent model but is less common in modern deployments.

tun mode (Layer 3)
++++++++++++++++++

In ``tun`` mode, OpenVPN operates at Layer 3. Whether VPN client source IPs arrive intact at the local network depends on the server-side routing configuration:

- **With proxy ARP or subnet routing without masquerading** — the client's source IP is preserved. Desomnia can identify individual clients and per-client filtering is possible. If the OpenVPN server is the default router, declare it as ``<Router>`` and optionally add ``<VPNClient>`` entries — see :doc:`../router`. If it runs on a dedicated device, no special configuration is required.
- **With NAT / masquerading** — all VPN traffic appears to originate from the VPN server's local IP. Per-client filtering is not possible. No special Desomnia configuration is needed.

Wake-on-LAN works in all configurations; Desomnia will see the connection attempt regardless of whether source IPs are preserved.

If your configuration includes a ``<ForeignHostFilterRule>`` and VPN clients are assigned IPs outside the local subnet, declare the VPN subnet as a ``<HostRange>`` to allow their traffic through. See :doc:`tailscale` for an example of this pattern.
