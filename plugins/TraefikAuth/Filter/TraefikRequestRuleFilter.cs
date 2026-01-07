using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Traefik.Filter.Rule;
using System.Net;

namespace MadWizard.Desomnia.Network.Traefik.Filter
{
    internal class TraefikRequestRuleFilter
    {
        public required IEnumerable<TraefikRequestFilterRule> Rules { protected get; init; }

        public bool ShouldFilter(HttpListenerRequest request)
        {
            bool needMatch = Rules.Any(rule => rule.Type == FilterRuleType.Must);

            foreach (var rule in Rules)
            {
                if (rule.Matches(request))
                {
                    if (rule.Type == FilterRuleType.MustNot)
                    {
                        return true;
                    }

                    if (rule.Type == FilterRuleType.Must || rule.Type == FilterRuleType.May)
                    {
                        needMatch = false; // no need to find a match anymore
                    }
                }
            }

            return needMatch;
        }

    }
}
