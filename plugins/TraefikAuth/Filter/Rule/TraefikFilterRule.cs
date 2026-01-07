using MadWizard.Desomnia.Network.Filter.Rules;
using System.Net;

namespace MadWizard.Desomnia.Network.Traefik.Filter.Rule
{
    public abstract class TraefikFilterRule : FilterRule
    {
        public abstract bool Matches(HttpListenerRequest request);
    }
}
