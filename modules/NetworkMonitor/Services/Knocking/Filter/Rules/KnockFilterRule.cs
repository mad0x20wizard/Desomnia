using MadWizard.Desomnia.Network.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking.Filter.Rules
{
    public abstract class KnockFilterRule : FilterRule
    {
        public abstract bool Matches(IPPacket packet, KnockEvent knock);
    }
}
