using MadWizard.Desomnia.Network.Device;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace MadWizard.Desomnia.Network.Tests;

internal static class FilterParserPackets
{
    internal static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");
    internal static readonly IPAddress Watched = IPAddress.Parse("192.0.2.20");
    internal static readonly PhysicalAddress PeerMac = PhysicalAddress.Parse("001122334455");
    internal static readonly PhysicalAddress WatchedMac = PhysicalAddress.Parse("AABBCCDDEEFF");

    internal static EthernetCapture Tcp(uint sequence, string data = "", bool syn = false,
        bool outbound = false, ushort servicePort = 80, bool ipv6 = false)
    {
        var tcp = new TcpPacket(outbound ? servicePort : (ushort)50000, outbound ? (ushort)50000 : servicePort)
        {
            SequenceNumber = sequence,
            Synchronize = syn,
            Acknowledgment = !syn || outbound,
            PayloadData = Encoding.Latin1.GetBytes(data),
        };
        return Transport(tcp, outbound, ipv6);
    }

    internal static EthernetCapture Udp(string data, bool outbound = false)
        => Transport(new UdpPacket(outbound ? (ushort)80 : (ushort)50000, outbound ? (ushort)50000 : (ushort)80)
        {
            PayloadData = Encoding.Latin1.GetBytes(data),
        }, outbound, false);

    internal static EthernetCapture Transport(TransportPacket transport, bool outbound, bool ipv6)
    {
        IPAddress peer = ipv6 ? IPAddress.Parse("2001:db8::10") : Peer;
        IPAddress watched = ipv6 ? IPAddress.Parse("2001:db8::20") : Watched;
        IPAddress source = outbound ? watched : peer, target = outbound ? peer : watched;
        IPPacket ip = ipv6 ? new IPv6Packet(source, target) : new IPv4Packet(source, target);
        ip.PayloadPacket = transport;
        return new(new EthernetPacket(outbound ? WatchedMac : PeerMac, outbound ? PeerMac : WatchedMac,
            ipv6 ? EthernetType.IPv6 : EthernetType.IPv4) { PayloadPacket = ip }.MakeCopy());
    }

    internal static EthernetCapture Arp() => new(new EthernetPacket(PeerMac, WatchedMac, EthernetType.Arp)
    {
        PayloadPacket = new ArpPacket(ArpOperation.Request, WatchedMac, Watched, PeerMac, Peer),
    });
}
