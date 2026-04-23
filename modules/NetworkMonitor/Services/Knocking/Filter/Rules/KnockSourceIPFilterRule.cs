using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking.Filter.Rules
{
    public class KnockSourceIPFilterRule : KnockFilterRule
    {
        public override bool Matches(IPPacket packet, KnockEvent knock)
        {
            if (!packet.SourceAddress.Equals(knock.SourceAddress))
            {
                return true;
            }

            return false;
        }
    }
}
