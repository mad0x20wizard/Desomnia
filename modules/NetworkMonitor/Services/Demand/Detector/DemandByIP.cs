using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByIP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(in CaptureSummary capture)
        {
            if ((capture.Ethernet.Type == EthernetType.IPv4 || capture.Ethernet.Type == EthernetType.IPv6) && capture.Extract<IPPacket>() is IPPacket ip)
                if (false
                    || ip.Protocol == ProtocolType.Tcp && ip.PayloadPacket is TcpPacket tcp && !tcp.Reset
                    || ip.Protocol == ProtocolType.Udp && ip.PayloadPacket is UdpPacket // all UDP packets

                    // PINGv4
                    || ip.Protocol == ProtocolType.Icmp && ip.PayloadPacket is IcmpV4Packet icmpv4
                        && icmpv4.TypeCode  == IcmpV4TypeCode.EchoRequest
                    // PINGv6
                    || ip.Protocol == ProtocolType.IcmpV6 && ip.PayloadPacket is IcmpV6Packet icmpv6
                        && icmpv6.Type      == IcmpV6Type.EchoRequest) 
                {
                    if (Network[capture.TargetAddress] is NetworkHost host)
                    {
                        return host;
                    }
                }

            return null;
        }
    }
}
