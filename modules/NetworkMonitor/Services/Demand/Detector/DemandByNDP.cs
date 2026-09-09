using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByNDP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(in CaptureSummary capture)
        {
            if (capture.Ethernet.Type == EthernetType.IPv6 && capture.Extract<IPv6Packet>() is IPv6Packet ipv6)
                if (ipv6.Protocol == ProtocolType.IcmpV6 && ipv6.PayloadPacket is IcmpV6Packet icmpv6)
                    if (icmpv6.Type == IcmpV6Type.NeighborSolicitation && icmpv6.PayloadPacket is NdpNeighborSolicitationPacket)
                    {
                        if (capture.IsDuplicateAddressDetection)
                            return null; // don't react to DAD

                        if (Network[capture.TargetAddress] is NetworkHost host)
                        {
                            if (Network[capture.SourcePhysicalAddress] is VirtualNetworkHost vhost && vhost.PhysicalHost == host)
                                return null; // address resolution for a virtual host's physical host

                            return host;
                        }
                    }

            return null;
        }
    }
}
