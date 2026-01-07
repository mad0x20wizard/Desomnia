using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network
{
    public class NetworkHostUsage(NetworkHost host, long bytes) : NetworkUsage(bytes)
    {
        public string Name => host.Name;

        public NetworkHost Host => host;

        public IList<NetworkServiceUsage> Services => [.. Tokens.OfType<NetworkServiceUsage>()];

        public override string ToString() => Name + (Services != null ? ":" + string.Join(':', Services) : string.Empty);
    }
}
