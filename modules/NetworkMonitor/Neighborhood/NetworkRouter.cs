using MadWizard.Desomnia.Network.Configuration.Options;
using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkRouter(string name) : NetworkHost(name)
    {
        public required RouterOptions Options { get; init; }

        public required IEnumerable<NetworkHost> VPNClients { get; init; }

        public DateTime? LastSeenVPN { get; internal set; }

        public NetworkHost? FindVPNClient(IPAddress? ip)
        {
            foreach (var host in VPNClients)
                if (host.HasAddress(ip: ip))
                    return host;

            return null;
        }
    }
}
