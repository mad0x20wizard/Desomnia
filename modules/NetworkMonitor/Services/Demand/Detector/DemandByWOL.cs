using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByWOL : IDemandDetector
    {
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(EthernetPacket packet)
        {
            if (packet.IsMagicPacket(out var mac))
            {
                if (Network[mac] is NetworkHost host)
                {
                    return host;
                }
            }

            return null;
        }
    }
}
