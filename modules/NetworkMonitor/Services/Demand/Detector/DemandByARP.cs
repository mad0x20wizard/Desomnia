using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByARP : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(in CaptureSummary capture)
        {
            if (capture.Ethernet.Type == EthernetType.Arp && capture.Extract<ArpPacket>() is ArpPacket arp)
            {
                if (arp.Operation != ArpOperation.Request)
                    return null;
                if (capture.IsARPGratuitous || capture.IsARPProbe || capture.IsARPAnnouncement)
                    return null;

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
