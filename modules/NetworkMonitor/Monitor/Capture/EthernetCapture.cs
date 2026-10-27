using PacketDotNet;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>
    /// Lazy metadata for an immutable captured packet. Constructing this object does not
    /// decode packet layers or materialize addresses, endpoints, or payload arrays.
    /// </summary>
    /// <remarks>
    /// Consumers share this cache and must not mutate its packet or cached values.
    /// Access to the lazy cache must be serialized by its owner.
    /// </remarks>
    public class EthernetCapture : INetworkCapture
    {
        [Flags]
        private enum Cached : byte
        {
            SourceAddress = 1 << 0,
            TargetAddress = 1 << 1,
            SourceEndPoint = 1 << 2,
            TargetEndPoint = 1 << 3,
            PayloadLength = 1 << 4,
        }

        private Cached _cached;
        private PhysicalAddress? _sourcePhysicalAddress, _targetPhysicalAddress;
        private IPAddress? _sourceAddress, _targetAddress;
        private IPEndPoint? _sourceEndPoint, _targetEndPoint;
        private byte[]? _payloadData;
        private int _payloadLength;

        internal readonly EthernetPacket _packet;

        // The representative frame is the offset-zero fragment for reassembled captures.
        // Header metadata must not call virtual FindPacket, which materializes the full datagram.
        private IPPacket? IP => _packet.Extract<IPPacket>();
        private ArpPacket? ARP => IP is null ? _packet.Extract<ArpPacket>() : null;
        private TransportPacket? Transport => IP?.Protocol is ProtocolType.Tcp or ProtocolType.Udp
            ? ExtractPacket<TransportPacket>() : null;
        private NdpPacket? NDP => IP?.Protocol == ProtocolType.IcmpV6
            ? ExtractPacket<NdpPacket>() : null;

        /// <summary>The original frame's outer Ethernet type, including any VLAN encapsulation.</summary>
        public EthernetType Type => _packet.Type;

        public PhysicalAddress SourcePhysicalAddress
        {
            get
            {
                return _sourcePhysicalAddress ??= _packet.SourceHardwareAddress;
            }
        }

        /// <summary>
        /// For WoL, the host named in the magic packet; otherwise the Ethernet destination.
        /// This payload-dependent lookup may materialize a reassembled packet.
        /// </summary>
        public PhysicalAddress TargetPhysicalAddress
        {
            get
            {
                return _targetPhysicalAddress ??= FindPacket<WakeOnLanPacket>()?.DestinationAddress ?? _packet.DestinationHardwareAddress;
            }
        }

        // Address classification alone need not allocate endpoints.
        public IPAddress? SourceAddress
        {
            get
            {
                if ((_cached & Cached.SourceAddress) == 0)
                {
                    _sourceAddress = ARP?.SenderProtocolAddress
                        ?? IP?.SourceAddress;

                    _cached |= Cached.SourceAddress;
                }

                return _sourceAddress;
            }
        }

        /// <summary>ARP/NDP resolution target when present, otherwise the IP destination.</summary>
        public IPAddress? TargetAddress
        {
            get
            {
                if ((_cached & Cached.TargetAddress) == 0)
                {
                    _targetAddress = ARP?.TargetProtocolAddress
                        ?? (NDP as NdpNeighborSolicitationPacket)?.TargetAddress
                        ?? IP?.DestinationAddress;

                    _cached |= Cached.TargetAddress;
                }

                return _targetAddress;
            }
        }

        /// <summary>
        /// Cached source address and port. Cast to IPServiceEndPoint for transport
        /// metadata; address-only traffic uses IPEndPoint with port zero (no usable port).
        /// Null means no source IP address is available. Consumers must not mutate the endpoint.
        /// </summary>
        public IPEndPoint? SourceEndPoint
        {
            get
            {
                if ((_cached & Cached.SourceEndPoint) == 0)
                {
                    _sourceEndPoint = CreateEndPoint(SourceAddress, source: true);

                    _cached |= Cached.SourceEndPoint;
                }

                return _sourceEndPoint;
            }
        }

        /// <summary>
        /// Cached target address and port, retaining ARP/NDP solicitation resolution targets.
        /// Cast to IPServiceEndPoint for transport metadata; address-only traffic uses
        /// IPEndPoint with port zero (no usable port). Null means no target IP address is available.
        /// Consumers must not mutate the endpoint.
        /// </summary>
        public IPEndPoint? TargetEndPoint
        {
            get
            {
                if ((_cached & Cached.TargetEndPoint) == 0)
                {
                    _targetEndPoint = CreateEndPoint(TargetAddress, source: false);

                    _cached |= Cached.TargetEndPoint;
                }

                return _targetEndPoint;
            }
        }

        /// <summary>Current transport payload size, without obtaining or copying PayloadData.</summary>
        public int PayloadLength
        {
            get
            {
                if ((_cached & Cached.PayloadLength) == 0)
                {
                    _payloadLength = ReadPayloadLength();

                    _cached |= Cached.PayloadLength;
                }

                return _payloadLength;
            }
        }

        /// <summary>
        /// Current transport payload, including decoded child packets. Materialized once
        /// on demand; consumers must not mutate the returned array.
        /// </summary>
        public byte[] PayloadData => _payloadData ??= ReadPayloadData();

        protected virtual int ReadPayloadLength()   => Transport?.PayloadLength ?? 0;

        protected virtual byte[] ReadPayloadData()  => Transport switch
        {
            { HasPayloadData: true } transport => transport.PayloadData,
            { PayloadPacket: { } payload } => payload.Bytes,
            _ => [],
        };

        // These classifications reuse cached addresses. Preserve the
        // existing CaptureSummary semantics, including its IPv4 multicast interpretation.
        public bool IsIPUnicast => IP is not null && NDP is null && TargetAddress is { } target
            && !target.Equals(IPAddress.Broadcast) && !target.IsIPv6Multicast;

        public EthernetCapture(EthernetPacket packet)
        {
            _packet = packet ?? throw new ArgumentNullException(nameof(packet));
        }

        protected virtual IPEndPoint? CreateEndPoint(IPAddress? address, bool source)
        {
            if (address is null)
                return null;

            switch (Transport)
            {
                case TcpPacket tcp:
                    return new IPServiceEndPoint(address, IPProtocol.TCP, source ? tcp.SourcePort : tcp.DestinationPort);
                case UdpPacket udp:
                    return new IPServiceEndPoint(address, IPProtocol.UDP, source ? udp.SourcePort : udp.DestinationPort);
                default:
                    return new IPEndPoint(address, 0);
            }
        }

        public virtual bool HasPacket<T>() where T : Packet => HasPacket<T>(out _);

        public virtual bool HasPacket<T>([NotNullWhen(true)] out T? packet) where T : Packet
        {
            return (packet = FindPacket<T>()) is not null;
        }

        public virtual T? FindPacket<T>() where T : Packet
        {
            return ExtractPacket<T>();
        }

        private T? ExtractPacket<T>() where T : Packet
        {
            for (Packet? packet = _packet; packet is not null; packet = packet.PayloadPacket)
            {
                if (packet is T result) return result;
                if (packet is IPPacket ip && !CanDecodePayload(ip)) return null;
            }
            return null;
        }

        private static bool CanDecodePayload(IPPacket ip)
        {
            if (ip is IPv4Packet v4)
            {
                if (v4.FragmentOffset != 0) return false;
                if ((v4.FragmentFlags & 1) == 0) return true;
            }
            else if (ip is IPv6Packet v6)
            {
                // Read the actual wire offset. PacketDotNet 1.4.8 can point extension
                // header objects at the first extension even when they occur later.
                var wire = v6.HeaderSegment;
                int pos = wire.Offset + 40;
                int limit = Math.Min(wire.BytesLength, pos + v6.PayloadLength);
                byte next = wire.Bytes[wire.Offset + 6];
                while (next != 44)
                {
                    if (next is not (0 or 43 or 60 or 51)) return true;
                    if (pos + 2 > limit) return false;
                    int size = next == 51 ? (wire.Bytes[pos + 1] + 2) * 4 : (wire.Bytes[pos + 1] + 1) * 8;
                    if (size < 8 || pos + size > limit) return false;
                    next = wire.Bytes[pos];
                    pos += size;
                }
                if (pos + 8 > limit || (IPFragment.U16(wire.Bytes, pos + 2) & 0xfff8) != 0) return false;
            }
            else return true;

            // A first fragment can contain only part of the transport header. Check wire
            // bounds before letting PacketDotNet's lazy decoder traverse into that payload.
            var header = ip.HeaderSegment;
            int start = header.Offset + header.Length;
            // Programmatically constructed packets keep their child in a separate buffer.
            if (start == header.BytesLength && ip.IsPayloadInitialized) return true;
            int end = header.Offset + (ip is IPv4Packet
                ? IPFragment.U16(header.Bytes, header.Offset + 2)
                : 40 + IPFragment.U16(header.Bytes, header.Offset + 4));
            int available = Math.Min(end, header.BytesLength) - start;
            return ip.Protocol switch
            {
                ProtocolType.Tcp => available >= 20 && (header.Bytes[start + 12] >> 4) >= 5
                    && available >= (header.Bytes[start + 12] >> 4) * 4,
                ProtocolType.Udp or ProtocolType.Icmp or ProtocolType.IcmpV6 => available >= 8,
                _ => available > 0,
            };
        }

        public virtual IEnumerator<EthernetPacket> GetEnumerator()
        {
            yield return _packet;
        }
    }
}
