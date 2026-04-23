using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking.Filter.Rules
{
    public class KnockTimeFilterRule(TimeSpan timeout) : KnockFilterRule
    {
        public override bool Matches(IPPacket packet, KnockEvent knock)
        {
            if (DateTime.Now - knock.Time > timeout)
            {
                return true;
            }

            return false;
        }
    }
}
