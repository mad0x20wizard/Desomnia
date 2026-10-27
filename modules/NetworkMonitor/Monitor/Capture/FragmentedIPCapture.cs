using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>
    /// One complete IP datagram backed by immutable original Ethernet fragments. Ethernet/IP
    /// metadata comes from the first fragment; transport headers and payload are read from slices.
    /// Decoded packet access creates a separate,
    /// cached normalized IP packet. Enumeration always returns the original frames.
    /// </summary>
    public sealed class FragmentedIPCapture : EthernetCapture
    {
        private readonly IPFragment[] _fragments;
        private readonly int _length;
        private IPPacket? _reassembled;
        private int _transportStart = -1, _payloadStart, _payloadEnd;
        private byte _protocol;

        // The reassembler supplies already validated, ordered, contiguous fragments.
        internal FragmentedIPCapture(IPFragment[] fragments) : base(fragments[0].Packet)
        {
            _fragments = fragments;
            _length = fragments[^1].Offset + fragments[^1].Length;
        }

        protected override IPEndPoint? CreateEndPoint(IPAddress? address, bool source)
        {
            if (address is not null)
            {
                ReadTransport();

                int offset = _transportStart + (source ? 0 : 2);

                switch (_protocol)
                {
                    case 6:
                        return new IPServiceEndPoint(address, IPProtocol.TCP, _fragments.ReadShort(offset));
                    case 17:
                        return new IPServiceEndPoint(address, IPProtocol.UDP, _fragments.ReadShort(offset));
                    default:
                        return new IPEndPoint(address, 0);
                }
            }

            return null;
        }

        protected override int ReadPayloadLength() { ReadTransport(); return _payloadEnd - _payloadStart; }

        protected override byte[] ReadPayloadData()
        {
            ReadTransport();
            var data = new byte[_payloadEnd - _payloadStart];
            CopyRange(_payloadStart, data);
            return data;
        }

        private void ReadTransport()
        {
            if (_transportStart >= 0) return;
            byte protocol = _fragments[0].GetTransport(out int pos);
            int payload = pos, end = pos;
            if (protocol == 6)
            {
                int header = (_fragments.ReadByte(pos + 12) >> 4) * 4;
                if (header < 20 || pos + header > _length) throw new FormatException("Invalid TCP header length.");
                payload = pos + header;
                end = _length;
            }
            else if (protocol == 17)
            {
                int size = _fragments.ReadShort(pos + 4);
                if (size < 8 || pos + size > _length) throw new FormatException("Invalid UDP length.");
                payload = pos + 8;
                end = pos + size;
            }
            _protocol = protocol;
            _payloadStart = payload;
            _payloadEnd = end;
            _transportStart = pos;
        }


        private void CopyRange(int offset, Span<byte> destination)
        {
            int end = offset + destination.Length;
            foreach (var f in _fragments)
            {
                int from = Math.Max(offset, f.Offset), to = Math.Min(end, f.Offset + f.Length);
                if (from < to) f.Data.Slice(from - f.Offset, to - from).CopyTo(destination[(from - offset)..]);
            }
        }

        public override bool HasPacket<T>() => GetPacketPresence<T>() ?? base.HasPacket<T>();

        public override T? FindPacket<T>() where T : class
        {
            if (GetPacketPresence<T>() != false) // This also keeps the inherited HasPacket(out packet) lazy for known absences.
            {
                return (_reassembled ??= ReassembleIPPacket()).Extract<T>();
            }

            return null;
        }

        private bool? GetPacketPresence<T>() where T : Packet
        {
            if (typeof(T) == typeof(EthernetPacket))
                throw new NotSupportedException("Enumerate the capture to access original Ethernet frames.");

            if (typeof(T) == typeof(ArpPacket)) return false;

            var ipType = _fragments[0] is IPv4Fragment ? typeof(IPv4Packet) : typeof(IPv6Packet);
            if (typeof(T).IsAssignableFrom(ipType)) return true;

            // Use protocol metadata, not the first frame's decoded tree: an IPv4
            // transport header can itself span several fragments.
            byte protocol = _fragments[0].GetTransport(out _);
            Type? transportType = protocol switch
            {
                6 => typeof(TcpPacket),
                17 => typeof(UdpPacket),
                1 => typeof(IcmpV4Packet),
                58 => typeof(IcmpV6Packet),
                _ => null,
            };
            if (transportType is not null && typeof(T).IsAssignableFrom(transportType)) return true;

            // TCP's decoded application payload cannot contain these network headers.
            // Other protocols can expose nested packets (for example UDP tunnels), so
            // a mismatching outer protocol alone is not sufficient to prove absence.
            if (protocol == 6 && (typeof(IPPacket).IsAssignableFrom(typeof(T))
                || typeof(TransportPacket).IsAssignableFrom(typeof(T))
                || typeof(NdpPacket).IsAssignableFrom(typeof(T))
                || typeof(T) == typeof(IcmpV4Packet) || typeof(T) == typeof(IcmpV6Packet)))
                return false;

            return null; // Payload inspection is needed; use the complete datagram.
        }

        private IPPacket ReassembleIPPacket()
        {
            var first = _fragments[0];
            int ecn = _fragments.Any(f => f.ECN == 3) ? 3 : first.ECN;
            int headerLength = first.HeaderLength;
            byte[] data = new byte[headerLength + _length];
            first.Bytes.AsSpan(first.IPStart, headerLength).CopyTo(data);
            CopyRange(0, data.AsSpan(headerLength));
            return first.CreatePacket(data, ecn);
        }

        public override IEnumerator<EthernetPacket> GetEnumerator()
        {
            foreach (var fragment in _fragments)
            {
                yield return fragment.Packet;
            }
        }
    }

    file static class FragmentsExt
    {
        extension(IEnumerable<IPFragment> fragments)
        {
            public byte ReadByte(int offset)
            {
                foreach (var f in fragments)
                    if (offset >= f.Offset && offset < f.Offset + f.Length) return f.Bytes[f.DataStart + offset - f.Offset];
                throw new FormatException("Truncated transport header.");
            }

            public ushort ReadShort(int offset) => (ushort)((fragments.ReadByte(offset) << 8) | fragments.ReadByte(offset + 1));
        }
    }
}
