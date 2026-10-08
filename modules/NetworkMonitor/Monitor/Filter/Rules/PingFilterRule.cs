using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    internal class PingFilterRule : IPFilterRule
    {
        override public bool Matches(EthernetPacket packet, PacketDirection direction = PacketDirection.Inbound)
        {
            if (base.Matches(packet, direction))
            {
                // IPv4-Support
                if (packet.PayloadPacket is IPv4Packet ip4 && ip4.PayloadPacket is IcmpV4Packet icmp4)
                {
                    var code = direction == PacketDirection.Inbound 
                        ? IcmpV4TypeCode.EchoRequest 
                        : IcmpV4TypeCode.EchoReply;

                    if (icmp4.TypeCode == code)
                        return true;
                }

                // IPv6-Support
                else if (packet.PayloadPacket is IPv6Packet ip6 && ip6.PayloadPacket is IcmpV6Packet icmp6)
                {
                    var code = direction == PacketDirection.Inbound
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
