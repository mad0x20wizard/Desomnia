using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Filter.Rules;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Services.Knocking.Filter.Rules
{
    public class KnockTimeFilterRule : KnockFilterRule
    {
        public override bool Matches(IPPacket packet, KnockEvent knock)
        {
            throw new NotImplementedException();
        }
    }
}
