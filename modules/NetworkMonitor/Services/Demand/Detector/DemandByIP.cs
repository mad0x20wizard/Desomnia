using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByIP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(EthernetPacket packet)
        {
            if ((packet.Type == EthernetType.IPv4 || packet.Type == EthernetType.IPv6) && packet.PayloadPacket is IPPacket ip)
                if (false
                    || ip.Protocol == ProtocolType.Tcp && ip.PayloadPacket is TcpPacket tcp
                        && !tcp.Reset
                    || ip.Protocol == ProtocolType.Udp // all UDP packets
                    // PINGv4
                    || ip.Protocol == ProtocolType.Icmp && ip.PayloadPacket is IcmpV4Packet icmpv4
                        && icmpv4.TypeCode == IcmpV4TypeCode.EchoRequest
                    // PINGv6
                    || ip.Protocol == ProtocolType.IcmpV6 && ip.PayloadPacket is IcmpV6Packet icmpv6
                        && icmpv6.Type == IcmpV6Type.EchoRequest) 
                {
                    if (Network[ip.DestinationAddress] is NetworkHost host)
                    {
                        return host;
                    }
                }

            return null;
        }
    }
}