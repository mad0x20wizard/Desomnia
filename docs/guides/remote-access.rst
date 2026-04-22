Remote Access
=============

:OS: 🪐 *Platform-independent*

Reaching a sleeping host from outside your local network requires solving two problems at once: getting the wake signal across the network boundary, and controlling which external sources are permitted to trigger a wake-up. The four approaches below address both, ordered from simplest to most sophisticated.

Port forwarding
---------------

The simplest path requires no VPN and no changes on the connecting device. Run Desomnia in :doc:`promiscuous mode </modules/network/promiscuous>` on an always-on device inside your network and forward one or more service ports on your router to that device. When an external client connects, the connection attempt arrives at the proxy, which sends the Magic Packet and wakes the sleeping host transparently.

Start with the :doc:`wol-proxy` guide if you have not done so already. Then declare your router and allow it to pass on externally-originating requests:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" allowWakeByProxy="true" />

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <Service name="SSH" port="22" />
     </RemoteHost>
   </NetworkMonitor>

``allowWakeByProxy="true"`` permits packets forwarded by the router — those carrying a source IP different from the router's own — to trigger wake-ups. Without it, Desomnia blocks all externally-originating traffic by default.

.. caution::

   With port forwarding alone, any host on the internet that reaches the forwarded port can trigger a wake-up. Add a ``<ForeignHostFilterRule>`` to restrict which source addresses are allowed through:

   .. code:: xml

      <ForeignHostFilterRule>
        <HostFilterRule name="my-laptop" IPv4="203.0.113.42" />
      </ForeignHostFilterRule>

   This is straightforward when your external IP is static and known. If your IP is dynamic or you connect from multiple locations, `Single Packet Authorization`_ is the right solution.

Unicast Magic Packets
---------------------

If your router supports **static IP-to-MAC address mappings**, Magic Packets can be sent as Layer 3 UDP unicast and routed across the network boundary — no always-on proxy required. Desomnia runs in client mode on the machine you connect from and handles the wake-up automatically.

When you try to connect to a sleeping host, Desomnia detects the attempt and sends a Magic Packet. With ``wakeType="auto"`` (the default), it checks whether the target IP is on the same subnet. If not, it sends the packet as a Layer 3 UDP unicast. The router uses its static ARP table to deliver the packet to the sleeping host's MAC address.

.. code:: xml

   <NetworkMonitor>
     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <Service name="SSH" port="22" />
     </RemoteHost>
   </NetworkMonitor>

This works in two scenarios:

- **Router-integrated VPN**: connect to your router's VPN first. The VPN makes the remote network reachable from your machine; Desomnia sends the Magic Packet as a unicast on the VPN interface and the router delivers it using its static ARP table.
- **WoL port forwarding**: some routers can accept and forward an external Magic Packet to an internal host's IP via a dedicated Wake-on-LAN forwarding feature. Forward the WoL port (UDP 9 by default) on your router and refer to your router's documentation for how to enable WoL from outside.

Follow the :doc:`wol-client` guide for a full walkthrough of client mode configuration.

.. note::

   Many consumer routers — including the FRITZ!Box — do not support static ARP entries, making routed Magic Packets impossible. If your router does not support this, use the proxy approach instead. See :doc:`/modules/network/router` and the individual router pages for compatibility details.

VPN
---

Running Desomnia in proxy mode alongside a VPN is the most common approach for regular remote access. The VPN tunnel delivers connection attempts inside the local network; the proxy detects them and sends Magic Packets. Authentication and encryption are handled entirely by the VPN.

Which Desomnia configuration is needed depends on how the VPN presents traffic on the local segment:

Router-integrated VPN
+++++++++++++++++++++

When the VPN server is your router, forwarded packets carry the router's MAC as their physical sender. Declare the router and add a ``<VPNClient>`` entry for each known client. Desomnia pings these before permitting wake-ups, confirming that at least one VPN client is actually connected:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" vpnTimeout="250ms">
       <VPNClient name="Alice" IPv4="192.168.1.201" />
     </Router>

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <Service name="RDP" port="3389" />
     </RemoteHost>
   </NetworkMonitor>

When none of the declared VPN clients respond to a ping, forwarded packets are blocked — preventing idle internet traffic from waking hosts when nobody is actually connected. This matters because some routers answer ARP requests for VPN client IPs even when the client is offline, so address resolution alone cannot be trusted. See :doc:`/modules/network/router` and the :doc:`/modules/network/routers/fritzbox` page for further details.

Standalone VPN server
+++++++++++++++++++++

A VPN server running on a dedicated device inside the network does not route traffic through the default gateway, so no ``<Router>`` declaration is needed. If the VPN is configured for proxy ARP or subnet routing without masquerading, source IPs are preserved and Desomnia can identify individual clients.

If VPN clients are assigned IPs outside the local subnet and a ``<ForeignHostFilterRule>`` is in place, declare the VPN range as a ``<HostRange>`` to let those clients through:

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <HostRange name="vpn-clients" network="10.8.0.0/24" />

     <ForeignHostFilterRule>
       <HostRangeFilterRule name="vpn-clients" />
     </ForeignHostFilterRule>

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <Service name="RDP" port="3389" />
     </RemoteHost>
   </NetworkMonitor>

See :doc:`/modules/network/vpn` for more details on the different VPN network models and how to configure each.

Masquerading
~~~~~~~~~~~~

When the VPN masquerades traffic — :doc:`/modules/network/vpn/tailscale` in its default configuration, or :doc:`/modules/network/vpn/openvpn` in tun mode with NAT — all VPN clients appear to originate from the VPN node's local IP. Individual clients cannot be identified and per-client filtering is not possible, but wake-on-demand still works: Desomnia sees the connection attempt regardless of which client originated it. No configuration beyond the baseline proxy setup is required.

Layer 2 bridge
~~~~~~~~~~~~~~

With an :doc:`/modules/network/vpn/openvpn` ``tap`` configuration, the VPN creates a Layer 2 bridge and VPN clients appear on the local network with their own MAC addresses. This is the most transparent model and equally requires no special Desomnia configuration — remote clients are indistinguishable from local ones.

Single Packet Authorization
----------------------------

If a VPN is not an option — or if latency matters for your application — Single Packet Authorization (SPA) offers a lighter alternative. Instead of a persistent tunnel, the client sends a single short UDP packet to prove its identity. Desomnia validates it and temporarily authorizes the client's IP address; any connection attempt from that IP to a watched host during the authorization window triggers a wake-up. No ongoing connection is required on either side.

This makes SPA particularly well suited to latency-sensitive workloads such as game streaming or remote desktop sessions, where even the per-packet overhead of a VPN tunnel is noticeable. It is also the right answer for the port-forwarding scenario when the external IP is not known in advance: SPA replaces the static ``<HostFilterRule>`` with dynamic, authenticated access.

.. note::

   SPA authenticates *access* — it does not encrypt the data connection itself. Combine it with application-level encryption (SSH, TLS) or a VPN for confidentiality.

SPA requires a proxy mode installation. Start with the :doc:`wol-proxy` guide if you have not done so already.

**Receiver** (always-on device, proxy mode):

.. code:: xml

   <NetworkMonitor watchMode="promiscuous">
     <Router name="gateway" MAC="B0:F2:08:0A:D1:14" IPv4="192.168.1.1" allowWakeByProxy="true" />

     <ForeignHostFilterRule>
       <DynamicHostRange name="trusted" knockMethod="plain" knockPort="12345" knockTimeout="30s">
         <SharedSecret encoding="UTF-8">changeme</SharedSecret>
       </DynamicHostRange>
     </ForeignHostFilterRule>

     <RemoteHost name="server" MAC="00:1A:2B:3C:4D:5E" IPv4="192.168.1.10">
       <Service name="SSH" port="22" />
     </RemoteHost>
   </NetworkMonitor>

Forward the knock port (UDP 12345 in this example) on your router to the always-on device.

**Sender** (machine you connect from):

.. code:: xml

   <NetworkMonitor knockDelay="200ms" knockRepeat="3s" knockTimeout="30s">
     <RemoteHost name="server"
       onServiceDemand="knock"
       knockMethod="plain" knockPort="12345"
       knockSecret="changeme" knockEncoding="UTF-8"
       IPv4="203.0.113.1">
       <Service name="SSH" port="22" />
     </RemoteHost>
   </NetworkMonitor>

Replace ``203.0.113.1`` with your router's public IP or use DNS based lookup via ``hostname``. When Desomnia detects a connection attempt to ``server``, it sends the knock automatically, waits for the host to wake up, and forwards the connection.

.. caution::

   The ``plain`` method transmits the shared secret as clear text with no replay protection. It is suitable for testing or when the knock traffic cannot be observed by an attacker. For any deployment exposed to the internet for an extended period, switch to the ``fko`` method, which adds AES encryption, HMAC authentication, and optional replay protection. See :doc:`/modules/network/knocking` for a comparison of the two methods and :doc:`/plugins/fko` for the fko configuration reference.
