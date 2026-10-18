using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public abstract class NetworkService(string name)
    {
        public string Name { get; init; } = name;

        public abstract bool Accepts(Packet packet, FlowDirection direction = FlowDirection.Inbound);
    }
}
