using MadWizard.Desomnia.Network.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    public class EveryHostFilterRule : PacketFilterRule
    {
        public IEnumerable<HostFilterRule> HostRules { get; set; } = [];

        override public bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            bool needMatch = HostRules.Any(rule => rule.Type == FilterRuleType.Must);

            foreach (HostFilterRule rule in HostRules)
            {
                if (rule.Matches(packet, direction))
                {
                    if (rule.Type == FilterRuleType.MustNot)
                        return false;
                    if (rule.Type == FilterRuleType.Must)
                        needMatch = false; // no need to find a match anymore
                }
            }

            return !needMatch;
        }
    }
}
