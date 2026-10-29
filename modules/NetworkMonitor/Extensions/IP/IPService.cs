using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;

namespace System.Net
{
    public readonly record struct IPService(IPProtocol Protocol, ushort Port)
    {
        public bool Accepts(TransportPacket packet)
        {
            switch (packet)
            {
                case TcpPacket tcp when Protocol.HasFlag(IPProtocol.TCP):
                    return tcp.DestinationPort == Port;
                case UdpPacket udp when Protocol.HasFlag(IPProtocol.UDP):
                    return udp.DestinationPort == Port;
            }

            return false;
        }

        public bool Accepts(TransportPacket packet, FlowDirection direction)
        {
            if (direction == FlowDirection.Inbound)
                return Accepts(packet);

            switch (packet) // outbound: the service answers from its own port
            {
                case TcpPacket tcp when Protocol.HasFlag(IPProtocol.TCP):
                    return tcp.SourcePort == Port;
                case UdpPacket udp when Protocol.HasFlag(IPProtocol.UDP):
                    return udp.SourcePort == Port;
            }

            return false;
        }

        public static IPService? SourceOf(TransportPacket packet)
        {
            switch (packet)
            {
                case TcpPacket tcp:
                    return new IPService(IPProtocol.TCP, tcp.SourcePort);
                case UdpPacket udp:
                    return new IPService(IPProtocol.UDP, udp.SourcePort);
            }

            return null;
        }

        public static IPService? DestinationOf(TransportPacket packet)
        {
            switch (packet)
            {
                case TcpPacket tcp:
                    return new IPService(IPProtocol.TCP, tcp.DestinationPort);
                case UdpPacket udp:
                    return new IPService(IPProtocol.UDP, udp.DestinationPort);
            }

            return null;
        }

        private string ToProtocolString()
        {
            return Protocol.ToString().ToLower().Replace(" ", "").Replace(",", "+");
        }

        public override string ToString()
        {
            return $"{Port}/{ToProtocolString()}";
        }
    }
}
