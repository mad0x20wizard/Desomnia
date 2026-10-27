using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    internal class ForeignHostFilterRule : EveryHostFilterRule
    {
        public required LocalNetworkRange LocalRange { get; init; }

        override public bool Matches(INetworkCapture packet, FlowDirection direction = FlowDirection.Inbound)
        {
            if (packet.HasPacket<IPPacket>())
            {
                var address = direction == FlowDirection.Inbound 
                    ? packet.SourceAddress
                    : packet.TargetAddress;

                if (address is not null && !LocalRange.Contains(address))
                {
                    return base.Matches(packet, direction);
                }
            }

            return false;
        }
    }
}
