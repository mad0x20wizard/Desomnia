using PacketDotNet;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public abstract class NetworkService(string name)
    {
        public string Name { get; init; } = name;

        public abstract bool Accepts(Packet packet);

        /// <summary>
        /// Direction-aware match, used for traffic accounting: inbound matches like
        /// <see cref="Accepts(Packet)"/>; only transport services can attribute a
        /// host's own outbound packets (by their source port).
        /// </summary>
        public virtual bool Accepts(Packet packet, PacketDirection direction)
        {
            return direction == PacketDirection.Inbound && Accepts(packet);
        }
    }
}
