using MadWizard.Desomnia.Network;
using PacketDotNet;

namespace System.Net
{
    public readonly record struct IPPort(IPProtocol Protocol, ushort Port)
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

        public bool Accepts(TransportPacket packet, PacketDirection direction)
        {
            if (direction == PacketDirection.Inbound)
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

        public static IPPort? SourceOf(TransportPacket packet)
        {
            switch (packet)
            {
                case TcpPacket tcp:
                    return new IPPort(IPProtocol.TCP, tcp.SourcePort);
                case UdpPacket udp:
                    return new IPPort(IPProtocol.UDP, udp.SourcePort);
            }

            return null;
        }

        public static IPPort? DestinationOf(TransportPacket packet)
        {
            switch (packet)
            {
                case TcpPacket tcp:
                    return new IPPort(IPProtocol.TCP, tcp.DestinationPort);
                case UdpPacket udp:
                    return new IPPort(IPProtocol.UDP, udp.DestinationPort);
            }

            return null;
        }

        public static implicit operator ushort(IPPort port) => port.Port;

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
