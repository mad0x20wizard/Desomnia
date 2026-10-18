using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class ServiceFilterRule(IPService port) : IPFilterRule
    {
        public IPService Port => port;

        override public bool Matches(EthernetPacket packet, FlowDirection direction = FlowDirection.Inbound)
        {
            if (packet.PayloadPacket is IPPacket ip && ip.PayloadPacket is TransportPacket transport)
            {
                if (Port.Accepts(transport, direction))
                {
                    return base.Matches(packet, direction);
                }
            }

            return false;
        }
    }
}
