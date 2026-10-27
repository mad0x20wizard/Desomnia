using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Neighborhood;

using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Detector
{
    internal class DemandByWOL : IDemandDetector
    {
        public required NetworkDevice Device { private get; init; }
        public required NetworkSegment Network { private get; init; }

        NetworkHost? IDemandDetector.Examine(INetworkCapture capture)
        {
            if (capture.IsMagicPacket(out var mac))
            {
                /*
                 * Since we use the UdpClient to send Magic Packets across network boundaries,
                 * we must filter out these packets, to avoid self-processing.
                 */
                if (Device.HasSentPacket(capture.SourcePhysicalAddress) && capture.HasPacket<UdpPacket>())
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
