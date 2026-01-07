using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByARP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(EthernetPacket packet)
        {
            if (packet.Type == EthernetType.Arp && packet.PayloadPacket is ArpPacket arp)
            {
                if (arp.Operation != ArpOperation.Request)
                    return null;
                if (arp.IsGratuitous() || arp.IsProbe() || arp.IsAnnouncement())
                    return null;

                if (Network[arp.TargetProtocolAddress] is NetworkHost host)
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
