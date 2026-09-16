using MadWizard.Desomnia.Network.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class PacketRuleFilter(IEnumerable<PacketFilterRule> rules) : IPacketFilter
    {
        public IEnumerable<PacketFilterRule> Rules => rules;

        readonly bool _blocksByDefault  = rules.Any(rule => rule.Type == FilterRuleType.Must);
        readonly bool _needsIPTraffic   = rules.Any(rule => rule is IPFilterRule);

        public virtual bool ShouldFilter(EthernetPacket packet, PacketFilterOptions options)
        {
            options.BlockByDefault |= _blocksByDefault;
            options.NeedsIPTraffic |= _needsIPTraffic;

            foreach (var rule in Rules)
            {
                if (rule.Matches(packet))
                {
                    if (rule.Type == FilterRuleType.MustNot)
                    {
                        return true;
                    }

                    if (rule.Type == FilterRuleType.Must)
                    {
                        options.BlockByDefault = false; // no need to find a match anymore
                    }
                }
            }

            if (options.NeedsIPTraffic && !packet.IsIPUnicast())
            {
                throw new IPUnicastNeededException(packet.FindTargetIPAddress()!);
            }

            return options.BlockByDefault;
        }
    }
}
