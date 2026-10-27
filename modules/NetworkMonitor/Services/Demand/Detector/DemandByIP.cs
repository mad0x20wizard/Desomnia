using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByIP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(INetworkCapture capture)
        {
            var ethernet = capture.First(); // only look at the first packet, so we don't trigger reassembly

            if (ethernet.Type == EthernetType.IPv4 || capture.Type == EthernetType.IPv6)
            {
                if (ethernet.PayloadPacket is IPPacket ip)
                {
                    switch (ip.PayloadPacket)
                    {
                        case TcpPacket tcp when !tcp.Reset:
                            return FindHostByIP(capture.TargetAddress);
                        case UdpPacket: // all UDP packets
                            return FindHostByIP(capture.TargetAddress);

                        case IcmpV4Packet icmpv4 when icmpv4.TypeCode == IcmpV4TypeCode.EchoRequest:
                            return FindHostByIP(capture.TargetAddress);

                        case IcmpV6Packet icmpv6 when icmpv6.Type == IcmpV6Type.EchoRequest:
                            return FindHostByIP(capture.TargetAddress);
                    }
                }
            }

            return null;
        }

        NetworkHost? FindHostByIP(IPAddress? ip)
        {
            if (Network[ip] is NetworkHost host)
            {
                return host;
            }

            return null;
        }
    }
}
