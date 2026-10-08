using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    internal class ForeignHostFilterRule(LocalNetworkRange lan) : EveryHostFilterRule
    {
        override public bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            if (packet.PayloadPacket is IPPacket ip)
            {
                var address = direction == PacketDirection.Inbound 
                    ? ip.SourceAddress 
                    : ip.DestinationAddress;

                if (!lan.Contains(address))
                {
                    return base.Matches(packet, direction);
                }
            }

            return false;
        }
    }
}
