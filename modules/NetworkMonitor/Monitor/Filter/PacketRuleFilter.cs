using MadWizard.Desomnia.Network.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class PacketRuleFilter : IPacketFilter
    {
        public required IEnumerable<PacketFilterRule> Rules { protected get; init; }

        public virtual bool ShouldFilter(EthernetPacket packet)
        {
            bool needMatch = Rules.Any(rule => rule.Type == FilterRuleType.Must);

            foreach (var rule in Rules)
            {
                if (rule.Matches(packet))
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

            if (Rules.Any(rule => rule is IPFilterRule) && !packet.IsIPUnicast())
            {
                throw new IPUnicastNeededException(packet.FindTargetIPAddress()!);
            }

            return needMatch;
        }
    }
}
