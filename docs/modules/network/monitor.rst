Network Monitor
===============

:OS: 🪟 *Windows* 🐧 *Linux* 🍎 *macOS*

The Network Monitor is the heart of Desomnia's wake-on-demand capability. It captures traffic at the Ethernet level using libpcap and reacts to connection attempts directed at sleeping hosts — sending a Magic Packet to wake them before the connecting client times out. A full reference of all available attributes is collected on the :doc:`configuration <config>` page.

At its most basic, the Network Monitor runs in **normal mode**: it watches the outgoing traffic of the machine it is installed on, and wakes hosts that this machine tries to reach. Switching to :doc:`promiscuous mode <promiscuous>` turns the same device into a **network-wide proxy** — any client on the segment can trigger a wake-up, with no software required on the connecting device. The interface binding, and which network segments are monitored, is controlled through :doc:`interface selection <interface>`.

Address mapping — knowing which MAC belongs to which IP — can be supplied statically in the configuration, or resolved automatically from your router's DNS and from traffic Desomnia observes on the wire. The :doc:`auto-configuration <auto>` page explains how to enable this. IPv6 addresses are supported alongside IPv4 and behave in the same way; the :doc:`IPv6 <ipv6>` page covers the cases where explicit configuration is needed.

In promiscuous mode, Desomnia may need to :doc:`claim a sleeping host's addresses <yield>` so that connection attempts are routed to the proxy rather than dropped. When a host is about to suspend, it can announce this via an *UnMagic Packet*, allowing the proxy to take over its addresses proactively rather than waiting for the first failed connection attempt.

For hosts reached through a router — including those accessed over a VPN — the :doc:`router configuration <router>` page explains how to declare the gateway and control which forwarded traffic is permitted to trigger wake-ups. :doc:`VPN support <vpn>` covers the different VPN network models (Layer 2 bridged, Layer 3 proxy ARP, Layer 3 masquerading) and what each means for how Desomnia identifies individual remote clients. Common router families have their own sub-pages under :doc:`router configuration <router>`.

When Desomnia is exposed to traffic from outside the local network, :doc:`knocking <knocking>` provides a Single Packet Authorization layer: only clients that present a valid credential are permitted to trigger a wake-up. The ``plain`` method is suitable for trusted networks; the ``fko`` method adds AES encryption, HMAC authentication, and optional replay protection for internet-facing deployments.

For environments with virtual machines, the :doc:`virtual machines <virtual>` page describes how to monitor VMs that use bridged networking. The :doc:`performance <performance>` page explains how Desomnia uses Berkeley Packet Filter rules to keep packet capture overhead minimal, and what to consider when many services or hosts are configured. If something is not working as expected, :doc:`troubleshooting <troubleshooting>` covers the most common failure modes.

.. toctree::
   config
   auto
   interface
   ipv6
   knocking
   promiscuous
   performance
   virtual
   router
   vpn
   yield
   troubleshooting

