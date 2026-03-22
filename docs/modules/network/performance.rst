Performance
===========

The Network Monitor internally uses `libpcap <https://en.wikipedia.org/wiki/Pcap>`__ to promiscuously capture network traffic at the local Ethernet link. This is a defacto industry standard for packet capture, used by various tools including the famous Wireshark. Many Linux systems come with libpcap already included. On Windows however, there is no default implementation available, which is why you have to install `npcap <https://npcap.com/>`__ manually (which will already be installed, if you use Wireshark).

The application itself is written in C# and uses `SharpPcap <https://github.com/chmorgan/sharppcap>`__ and `PacketNet <https://github.com/chmorgan/packetnet>`__ internally to communicate with the native libpcap implementation of your system and to parse the structure of incoming packets. These libraries were all written with performance in mind, because processing network traffic can scale very badly in high throughput situations.

To further reduce the load on your CPU, Desomnia uses a facility to apply BPF (`Berkeley Packet Filter <https://en.wikipedia.org/wiki/Berkeley_Packet_Filter>`__) rules directly inside the kernel module of libpcap, to prefilter all traffic we are not even remotely interested in. For example: if you don't use any ``ServiceFilterRule`` with UDP protocol type, we can safely skip all UDP traffic altogether, which means sparing the kernel the burden to copy theses packets over to the user space, just to get processed and ignored by our application code. And since we are only interested in port numbers when filtering TCP requests, all traffic but the inital handshake packet is dropped by default.

With these optimizations in place, you can be assured that Desomnia will never use more than a small amount of valuable CPU time, even when the system is under heavy network load.
