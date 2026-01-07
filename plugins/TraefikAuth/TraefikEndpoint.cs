using MadWizard.Desomnia.Network.Traefik.Filter;
using System.Net;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.Traefik
{
    internal class TraefikEndpoint
    {
        public required NetworkHostWatch Watch { private get; init; }

        public required string AuthPrefix { get; init; }
        public required Regex? MatchURL { get; init; }

        public required Lazy<ITraefikRequestFilter> Filter { private get; init; }

        public bool Accepts(HttpListenerRequest auth)
        {
            throw new NotImplementedException();
        }
    }
}
