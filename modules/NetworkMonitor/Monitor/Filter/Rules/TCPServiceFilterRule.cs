using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class TCPServiceFilterRule(ushort port) : TransportFilterRule(new IPPort(IPProtocol.TCP, port))
    {
        public override bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            if (base.Matches(packet, direction) && packet.Extract<TcpPacket>() is TcpPacket tcp)
            {
                return MatchesPayload(packet.PayloadPacket.PayloadData); // LATER: Implement TCP stream reassembly
            }

            return false;
        }

        protected virtual bool MatchesPayload(byte[] payload)
        {
            return true;
        }
    }
}
