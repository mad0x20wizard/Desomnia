using MadWizard.Desomnia.Network.Filter.Rules;
using System.Net;

namespace MadWizard.Desomnia.Network.Traefik.Filter.Rule
{
    internal class TraefikRequestFilterRule(HTTPRequestFilterRule http) : TraefikFilterRule
    {
        public override bool Matches(HttpListenerRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
