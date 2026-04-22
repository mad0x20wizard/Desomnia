Tailscale
=========

Tailscale is a mesh VPN that connects devices over WireGuard tunnels. Its behaviour with Desomnia depends on whether subnet routing SNAT (masquerading) is enabled or disabled.

Network model
-------------

Default (masquerading)
++++++++++++++++++++++

In the default configuration, when a Tailscale node advertises a local subnet, all traffic from remote clients is masqueraded: the source IP is replaced with the advertising node's local IP before the packet reaches the local network. Desomnia cannot distinguish individual VPN clients — all VPN traffic appears to originate from the Tailscale node.

Wake-on-LAN still works, because Desomnia still sees the connection attempt and can send a Magic Packet. Per-client filtering is not possible. No special Desomnia configuration is needed; the Tailscale node is treated as any other local device. If your configuration includes a ``<ForeignHostFilterRule>``, no additional steps are required — the Tailscale node's local IP is on the local subnet and passes the filter.

With ``--snat-subnet-routes=false``
++++++++++++++++++++++++++++++++++++

Disabling SNAT on the subnet-advertising node preserves remote clients' original source IPs. Their tailnet IPs (in the range ``100.64.0.0/10``) arrive intact at the local network. The Tailscale node handles proxy ARP for those IPs, so Desomnia sees the client's tailnet IP with the Tailscale node's MAC as the physical sender. Individual clients are identifiable by tailnet IP, and per-client filtering is possible.

To apply the setting:

.. code:: bash

   tailscale set --snat-subnet-routes=false

Tailnet IPs (``100.64.0.0/10``) are not part of the local subnet and are treated as foreign hosts. If your configuration includes a ``<ForeignHostFilterRule>``, declare the tailnet subnet as a ``<HostRange>`` to allow those clients through:

.. code:: xml

   <NetworkMonitor interface="eth0">

     <HostRange name="Tailscale" network="100.64.0.0/10">
       <Host name="Alice" hostname="alice.example.ts.net" autoDetect="IPv4" />
     </HostRange>

     <ForeignHostFilterRule>
       <HostRangeFilterRule name="Tailscale" />
     </ForeignHostFilterRule>

   </NetworkMonitor>

The named ``<Host>`` inside the range is optional. Without it, the raw tailnet IP appears in log output when that client triggers a wake-up. Declaring a named host with a ``hostname`` allows Desomnia to resolve it to a meaningful label.

If your configuration does not include a ``<ForeignHostFilterRule>`` at all, tailnet traffic passes through without any additional configuration.

Summary
-------

.. list-table::
   :header-rows: 1
   :widths: 45 15 40

   * - Feature
     - Support
     - Notes
   * - VPN client source IPs visible
     - ⚠️ Optional
     - Only with ``--snat-subnet-routes=false``; tailnet IPs in ``100.64.0.0/10``
   * - Per-client filtering
     - ⚠️ Optional
     - Only with ``--snat-subnet-routes=false``
   * - ``<ForeignHostFilterRule>`` interaction
     - ⚠️ Requires ``<HostRange>``
     - Only when source IPs are preserved; not an issue in default masquerading mode
