using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByWOL : IDemandDetector
    {
        public required NetworkDevice Device { private get; init; }
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(EthernetPacket packet)
        {
            if (packet.IsMagicPacket(out var mac))
            {
                /*
                 * Since we use the UdpClient to send Magic Packets across network boundaries,
                 * we must filter out these packets, to avoid self-processing.
                 */
                if (Device.HasSentPacket(packet) && packet.Extract<UdpPacket>() != null)
                    return null;

                if (Network[mac] is NetworkHost host)
                {
                    return host;
                }
            }

            return null;
        }
    }
}
