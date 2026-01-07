using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood.Services
{
    public class TransportNetworkService(string name, IPPort port) : NetworkService(name)
    {
        protected internal virtual IEnumerable<IPPort> Ports
        {
            get
            {
                yield return port;
            }
        }

        public override bool Accepts(Packet packet)
        {
            if (packet.Extract<TransportPacket>() is TransportPacket transport)
                return Ports.Any(service => service.Accepts(transport));

            return false;
        }

        public bool Serves(IPPort another) => Ports.Any(service => service == another);

        public override string ToString()
        {
            return $"{port} (\"{Name}\")";
        }
    }
}
