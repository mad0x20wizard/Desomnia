using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class TransportFilterRule(IPPort port) : IPFilterRule
    {
        public IPPort Port => port;

        override public bool Matches(EthernetPacket packet)
        {
            if (packet.PayloadPacket is IPPacket ip && ip.PayloadPacket is TransportPacket transport)
            {
                if (Port.Accepts(transport))
                {
                    return base.Matches(packet);
                }
            }

            return false;
        }
    }
}
