using MadWizard.Desomnia.Network.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    public class EveryHostFilterRule : PacketFilterRule
    {
        public IEnumerable<HostFilterRule> HostRules { get; set; } = [];

        override public bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            return MatchesRules(HostRules, packet, direction);
        }
    }
}
