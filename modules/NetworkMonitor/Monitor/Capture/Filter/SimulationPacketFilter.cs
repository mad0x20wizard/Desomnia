using MadWizard.Desomnia.Network.Filter;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;

namespace MadWizard.Desomnia.Network
{
    internal class SimulationPacketFilter : IDevicePacketFilter
    {
        public required ILogger<SimulationPacketFilter> Logger { private get; init; }

        public bool FilterIncoming(PacketCapture packet) => false;

        public bool FilterOutgoing(Packet packet) => true;
    }
}
