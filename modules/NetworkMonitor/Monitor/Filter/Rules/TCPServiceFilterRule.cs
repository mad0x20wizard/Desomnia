using MadWizard.Desomnia.Network.Capture;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class TCPServiceFilterRule(ushort port) : ServiceFilterRule(new IPService(IPProtocol.TCP, port))
    {
        public override bool Matches(INetworkCapture packet, FlowDirection direction = FlowDirection.Inbound)
        {
            if (base.Matches(packet, direction) && packet.HasPacket<TcpPacket>())
            {
                return MatchesPayload(packet.PayloadData);
            }

            return false;
        }

        protected virtual bool MatchesPayload(byte[] payload)
        {
            return true;
        }
    }
}
