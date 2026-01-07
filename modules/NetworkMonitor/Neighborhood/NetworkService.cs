using PacketDotNet;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public abstract class NetworkService(string name)
    {
        public string Name => name;

        public string? ServiceName { get; init; }

        public abstract bool Accepts(Packet packet);
    }
}
