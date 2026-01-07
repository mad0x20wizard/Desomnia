using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    internal class ForeignHostFilterRule(LocalNetworkRange lan) : NestedHostFilterRule
    {
        override public bool Matches(EthernetPacket packet)
        {
            if (packet.PayloadPacket is IPPacket ip)
            {
                if (!lan.Contains(ip.SourceAddress))
                {
                    return base.Matches(packet);
                }
            }

            return false;
        }
    }
}
