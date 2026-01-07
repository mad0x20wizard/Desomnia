using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking.Filter
{
    internal class KnockRuleFilter : IKnockFilter
    {
        public required IEnumerable<KnockFilterRule> Rules { protected get; init; }

        public bool ShouldFilter(IPPacket packet, KnockEvent knock)
        {
            bool needMatch = Rules.Any(rule => rule.Type == FilterRuleType.Must);

            foreach (var rule in Rules)
            {
                if (rule.Matches(packet, knock))
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
