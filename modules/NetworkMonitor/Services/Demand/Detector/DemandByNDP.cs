using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByNDP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(EthernetPacket packet)
        {
            if (packet.Type == EthernetType.IPv6 && packet.PayloadPacket is IPv6Packet ipv6)
                if (ipv6.Protocol == ProtocolType.IcmpV6 && ipv6.PayloadPacket is IcmpV6Packet icmpv6)
                    if (icmpv6.Type == IcmpV6Type.NeighborSolicitation && icmpv6.PayloadPacket is NdpNeighborSolicitationPacket sol)
                    {
                        if (ipv6.SourceAddress.Equals(IPAddress.IPv6Any))
                            return null; // don't react to DAD

                        if (Network[sol.TargetAddress] is NetworkHost host)
                        {
                            if (Network[packet.SourceHardwareAddress] is VirtualNetworkHost vhost && vhost.PhysicalHost == host)
                                return null; // address resolution for a virtual host's physical host

                            return host;
                        }
                    }

            return null;
        }
    }
}
