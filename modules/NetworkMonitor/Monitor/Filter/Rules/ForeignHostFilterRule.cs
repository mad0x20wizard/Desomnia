using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    internal class ForeignHostFilterRule : EveryHostFilterRule
    {
        public required LocalNetworkRange LocalRange { get; init; }

        override public bool Matches(EthernetPacket packet, FlowDirection direction = FlowDirection.Inbound)
        {
            if (packet.PayloadPacket is IPPacket ip)
            {
                var address = direction == FlowDirection.Inbound 
                    ? ip.SourceAddress 
                    : ip.DestinationAddress;

                if (!LocalRange.Contains(address))
                {
                    return base.Matches(packet, direction);
                }
            }

            return false;
        }
    }
}
