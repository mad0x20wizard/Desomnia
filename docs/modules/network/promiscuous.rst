Promiscuous mode
================

The ``watchMode`` attribute determines how the NetworkMonitor observes traffic and what it can react to. The two modes differ in what they can see and, as a consequence, which devices on the network can trigger a wake-up.

Normal mode
-----------

In normal mode, Desomnia watches only the outgoing traffic of the machine it runs on. When a program on that machine tries to connect to a remote host that is offline, Desomnia detects the failed connection attempt and sends a Magic Packet to wake it.

This is straightforward to set up and has no special requirements on the network. The limitation is scope: only the machine running Desomnia can trigger a wake-up. Other devices on the network — phones, tablets, smart TVs — cannot, unless they also run something similar.

For the client setup, see :doc:`/guides/wol-client`.

Promiscuous mode
----------------

In promiscuous mode, Desomnia places the network interface into a state where it receives *all* traffic on the local network segment, not just its own. This turns it into a transparent wake proxy: any device on the network can trigger a wake-up, without any configuration on the connecting device.

A single always-on device — a Raspberry Pi, a NAS, a small home server — can handle Wake-on-LAN for the entire network on behalf of every client.

For the proxy setup, see :doc:`/guides/wol-proxy`.

.. _network-monitor-spoofing:

How the proxy works
-------------------

For this to work, Desomnia needs to intercept connection attempts directed at sleeping hosts before they disappear unnoticed. To understand how, it helps to know how devices find each other on a local network.

When a device wants to send data to another device on the same network, it first needs to know the recipient's physical address (MAC address). It broadcasts a question to the whole network: *"Who has IP address X?"* The owner of that address responds with its MAC address, and communication proceeds normally. This mechanism is called `ARP`_ for IPv4 and `NDP`_ for IPv6.

A sleeping host does not respond to these queries. Without an answer, the connecting device concludes the target is unreachable and gives up — no connection attempt ever reaches Desomnia to act on.

Because Desomnia receives all traffic on the network segment in promiscuous mode, it sees these unanswered address queries as they happen. That alone is enough to detect a connection attempt and send a Magic Packet. **This passive observation requires no modification to the network whatsoever** — Desomnia simply watches and reacts.

Passive observation is sufficient as long as your configuration does not include IP-layer protocol filters: ``<Service>``, ``<ServiceFilterRule>``, and ``<PingFilterRule>`` all require inspecting the actual content of a packet, not just the address query. If your filters are based only on host names or IP addresses, Desomnia operates entirely passively and address claiming is never used.

**Address claiming**

When service or protocol filters *are* configured, passive observation is not enough. To inspect the actual connection — checking the destination port, protocol, or ICMP type — Desomnia must receive the data packets themselves, not just the address queries. To make that possible, it responds to address queries on behalf of the sleeping host, temporarily claiming its IP addresses so that packets arrive at Desomnia for inspection.

Once the host wakes up and starts answering address queries itself, it naturally reclaims its own IP addresses. Desomnia detects this and steps aside. At that point normal network traffic resumes exactly as if Desomnia had never been involved — packets are routed and switched directly between devices as usual. Desomnia is not a permanent proxy and does not sit in the path of live traffic.

**Safety checks**

Before claiming an address, Desomnia confirms that the host is genuinely offline and not responding to queries. It will not claim an address that belongs to a live host. The claim is temporary, scoped to the local network, and has no effect on routing or traffic outside the local broadcast domain.

This is the same mechanism that network infrastructure routinely uses for legitimate purposes — virtual machine migration, load balancers, failover clusters. Desomnia uses it in a targeted and reversible way: only while the host is confirmed offline, and only long enough to decide whether to wake it.

Benefits and trade-offs
------------------------

Compared to client mode, promiscuous mode offers:

- **Any device can trigger a wake-up** — phones, laptops, and other devices on the network work without any configuration changes on their end.
- **Centralised management** — one always-on device handles Wake-on-LAN for the whole network.
- **Smarter filtering** — when address claiming is active, Desomnia intercepts the actual connection attempt and can apply service filters before deciding to wake the host.
- **Minimal footprint** — Desomnia is not a permanent proxy. It only steps in when a sleeping host needs to be woken, and steps back out once the host is online. Live traffic flows directly between devices at all times.

The essential trade-off is that **the proxy device must remain online at all times**. If it goes to sleep or becomes unavailable, Wake-on-LAN stops working for the entire network. For this reason, promiscuous mode is best suited to a device that is permanently powered — a home server, a NAS, or a dedicated low-power device.

A note on environments
-----------------------

Responding to address queries on behalf of other hosts is standard network behaviour in many contexts, but it can appear unusual to security monitoring tools. In a managed enterprise environment it is likely to trigger alerts and may violate network policy.

Promiscuous mode is intended for home networks or small private networks where you have full administrative control. Do not use it on corporate networks, shared infrastructure, or any environment where network behaviour is centrally monitored or governed by policy you do not control.

.. _`ARP`: https://en.wikipedia.org/wiki/Address_Resolution_Protocol
.. _`NDP`: https://en.wikipedia.org/wiki/Neighbor_Discovery_Protocol
