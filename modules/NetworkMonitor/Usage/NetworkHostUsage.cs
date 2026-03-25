using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network
{
    public class NetworkHostUsage(NetworkHost host, long bytes) : NetworkUsage(bytes)
    {
        public string Name => host.Name;

        public NetworkHost Host => host;

        private IEnumerable<NetworkServiceUsage> ServiceTokens => Tokens.OfType<NetworkServiceUsage>();

        public override string ToString() => Name + (ServiceTokens.Any() ? ":" + string.Join(':', ServiceTokens) : string.Empty);
    }
}
