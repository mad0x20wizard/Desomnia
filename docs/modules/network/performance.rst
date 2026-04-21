Performance
===========

The NetworkMonitor uses `libpcap <https://en.wikipedia.org/wiki/Pcap>`__ to capture network packets at the Ethernet level. libpcap is a widely used industry standard for packet capture, found in tools such as Wireshark. On Linux and macOS it is typically already present; on Windows, `npcap <https://npcap.com/>`__ provides the equivalent implementation and is installed automatically by the Desomnia installer.

Internally, Desomnia uses `SharpPcap <https://github.com/chmorgan/sharppcap>`__ and `PacketNet <https://github.com/chmorgan/packetnet>`__ to communicate with libpcap and to parse the structure of captured packets. Both libraries are designed with performance in mind.

Berkeley Packet Filter
-----------------------

:OS: 🪟 *Windows* 🐧 *Linux* 🍎 *macOS*

The most significant performance optimisation in the NetworkMonitor is its use of `Berkeley Packet Filter <https://en.wikipedia.org/wiki/Berkeley_Packet_Filter>`__ (BPF) rules. BPF allows Desomnia to declare its filtering criteria directly inside the kernel's packet capture module, so that packets it does not need are discarded before they are ever copied to user space. Only packets that pass the filter are handed to the application.

This matters because copying packets from kernel space to user space has a cost regardless of whether the application ultimately uses them. By keeping the filter as tight as possible, Desomnia avoids imposing any overhead from traffic it has no interest in.

TCP services
++++++++++++

When only TCP-based services are configured, the BPF filter is at its most efficient. Desomnia needs to see only two categories of traffic:

- **TCP SYN packets** — the opening handshake of every new TCP connection, which signals a connection attempt to a watched host
- **ARP and NDP packets** — address resolution broadcasts, used to detect when a host comes online or to claim addresses on behalf of sleeping hosts

All other TCP traffic — payload, acknowledgements, retransmissions — is dropped in the kernel before it reaches Desomnia. A large file transfer over TCP, for example, generates no load on Desomnia regardless of its size.

UDP services
++++++++++++

UDP presents a different challenge. Unlike TCP, which uses a distinct handshake packet to signal a new connection, UDP has no connection concept — every packet is independent and the operating system cannot distinguish a connection attempt from a payload packet. When any UDP service is configured, the BPF filter must therefore pass all UDP traffic for inspection in user space.

Currently, Desomnia applies the UDP exception at the protocol level: if any UDP service is configured, all UDP traffic is captured regardless of port. Port-level BPF filtering for UDP is planned for a future release.

In practice this is rarely a concern, since most UDP protocols used for service monitoring have modest traffic volumes. However, configuring a watched service on a port that carries high-throughput UDP traffic — such as video streaming or real-time data feeds — can cause a noticeable increase in CPU usage. If you observe elevated CPU load and have UDP services configured, this is the likely cause. The only mitigation at present is to avoid configuring UDP services on high-throughput ports.

Local resource management
--------------------------

A second situation where BPF optimisation does not apply is local resource management. When a network host is configured as a watched resource for sleep management — meaning Desomnia monitors its traffic to contribute to the system idle state — every packet to and from that host must be counted. Selective filtering is not possible in this case, and BPF filtering is effectively disabled for traffic involving that host.

At present, local resource management targets are limited to Windows hosts. If this feature is extended to other platforms in the future, the same considerations will apply.
