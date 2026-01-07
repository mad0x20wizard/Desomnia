using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    internal class PingFilterRule : IPFilterRule
    {
        override public bool Matches(EthernetPacket packet)
        {
            if (base.Matches(packet))
            {
                // IPv4-Support
                if (packet.PayloadPacket is IPv4Packet ip4 && ip4.PayloadPacket is IcmpV4Packet icmp4)
                {
                    if (icmp4.TypeCode == IcmpV4TypeCode.EchoRequest)
                        return true;
                }

                // IPv6-Support
                else if (packet.PayloadPacket is IPv6Packet ip6 && ip6.PayloadPacket is IcmpV6Packet icmp6)
                {
                    if (icmp6.Type == IcmpV6Type.EchoRequest)
                        return true;
                }
            }

            return false;
        }
    }
}
