using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    public class EveryHostFilterRule : PacketFilterRule
    {
        public IEnumerable<HostFilterRule> HostRules { get; init; } = [];

        override public bool Matches(INetworkCapture packet, FlowDirection direction = FlowDirection.Inbound)
        {
            return MatchesRules(HostRules, packet, direction);
        }
    }
}
