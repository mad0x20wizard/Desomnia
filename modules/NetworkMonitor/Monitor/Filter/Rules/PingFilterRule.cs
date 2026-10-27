using MadWizard.Desomnia.Network.Capture;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    internal class PingFilterRule : IPFilterRule
    {
        override public bool Matches(INetworkCapture packet, FlowDirection direction = FlowDirection.Inbound)
        {
            if (base.Matches(packet, direction))
            {
                // IPv4-Support
                if (packet.HasPacket(out IcmpV4Packet? icmp4))
                {
                    var code = direction == FlowDirection.Inbound 
                        ? IcmpV4TypeCode.EchoRequest 
                        : IcmpV4TypeCode.EchoReply;

                    if (icmp4.TypeCode == code)
                        return true;
                }

                // IPv6-Support
                else if (packet.HasPacket(out IcmpV6Packet? icmp6))
                {
                    var code = direction == FlowDirection.Inbound
                        ? IcmpV6Type.EchoRequest
                        : IcmpV6Type.EchoReply;

                    if (icmp6.Type == code)
                        return true;
                }
            }

            return false;
        }
    }
}
