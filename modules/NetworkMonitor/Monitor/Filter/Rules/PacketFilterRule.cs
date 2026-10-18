using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class PacketFilterRule : FilterRule
    {
        public abstract bool Matches(EthernetPacket packet, FlowDirection direction = FlowDirection.Inbound);

        protected static bool MatchesRules(IEnumerable<PacketFilterRule> rules, EthernetPacket packet, FlowDirection direction)
        {
            bool needMatch = rules.Any(rule => rule.Type == FilterRuleType.Must);

            foreach (var rule in rules)
            {
                if (rule.Matches(packet, direction))
                {
                    if (rule.Type == FilterRuleType.MustNot)
                    {
                        return false;
                    }

                    if (rule.Type == FilterRuleType.Must)
                    {
                        needMatch = false; // no need to find a match anymore
                    }
                }
            }

            return !needMatch;
        }
    }
}
