using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking.Filter.Rules
{
    internal class KnockPortFilterRule : KnockFilterRule
    {
        public override bool Matches(IPPacket packet, KnockEvent knock)
        {
            throw new NotImplementedException();
        }
    }
}
