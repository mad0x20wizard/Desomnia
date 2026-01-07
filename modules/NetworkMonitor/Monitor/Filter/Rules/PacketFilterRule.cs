using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class PacketFilterRule : FilterRule
    {
        public abstract bool Matches(EthernetPacket packet);
    }
}
