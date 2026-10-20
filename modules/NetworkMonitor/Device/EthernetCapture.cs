using PacketDotNet;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Device
{
    /// <summary>
    /// Lazy metadata for an immutable captured packet. Constructing this value does not
    /// decode packet layers or materialize addresses, endpoints, or payload arrays.
    /// </summary>
    /// <remarks>
    /// Pass the canonical value by ref during synchronous processing. An in parameter,
    /// readonly field, or value-returning collection access can cause a defensive copy
    /// whose cache writes do not reach the original.
    /// Before asynchronous retention, explicitly copy the value into owned storage.
    /// A copy shares the immutable packet and already cached references, but subsequent
    /// cache initialization is independent. Access to one mutable cache is not thread-safe.
    /// </remarks>
    public struct EthernetCapture
    {
        [Flags]
        private enum Cached : uint
        {
            IP = 1 << 0,
            Arp = 1 << 1,
            Transport = 1 << 2,
            Tcp = 1 << 3,
            Udp = 1 << 4,
            Ndp = 1 << 5,
            WakeOnLan = 1 << 6,
            SourceAddress = 1 << 7,
            TargetAddress = 1 << 8,
            SourceEndPoint = 1 << 9,
            TargetEndPoint = 1 << 10,
            PayloadLength = 1 << 11,
        }

        private Cached _cached;
        private IPPacket? _ip;
        private ArpPacket? _arp;
        private TransportPacket? _transport;
        private TcpPacket? _tcp;
        private UdpPacket? _udp;
        private NdpPacket? _ndp;
        private WakeOnLanPacket? _wakeOnLan;
        private PhysicalAddress? _sourcePhysicalAddress, _targetPhysicalAddress;
        private IPAddress? _sourceAddress, _targetAddress;
        private IPEndPoint? _sourceEndPoint, _targetEndPoint;
        private byte[]? _payloadData;
        private int _payloadLength;

        internal readonly EthernetPacket Packet { get; }

        private IPPacket? IP => GetPacket(ref _ip, Cached.IP);
        private ArpPacket? ARP => IP is null ? GetPacket(ref _arp, Cached.Arp) : null;
        private TransportPacket? Transport => IP?.Protocol is ProtocolType.Tcp or ProtocolType.Udp
            ? GetPacket(ref _transport, Cached.Transport) : null;
        private NdpPacket? NDP => IP?.Protocol == ProtocolType.IcmpV6
            ? GetPacket(ref _ndp, Cached.Ndp) : null;

        public PhysicalAddress SourcePhysicalAddress => _sourcePhysicalAddress ??= Packet.SourceHardwareAddress;

        /// <summary>For WoL, the host named in the magic packet; otherwise the Ethernet destination.</summary>
        public PhysicalAddress TargetPhysicalAddress => _targetPhysicalAddress ??=
            GetPacket(ref _wakeOnLan, Cached.WakeOnLan)?.DestinationAddress ?? Packet.DestinationHardwareAddress;

        // Keep address caching private so classification alone need not allocate endpoints.
        private IPAddress? SourceAddress
        {
            get
            {
                if ((_cached & Cached.SourceAddress) == 0)
                {
                    _sourceAddress = ARP?.SenderProtocolAddress ?? IP?.SourceAddress;
                    _cached |= Cached.SourceAddress;
                }
                return _sourceAddress;
            }
        }

        /// <summary>ARP/NDP resolution target when present, otherwise the IP destination.</summary>
        private IPAddress? TargetAddress
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
        /// Cached source address and port. Cast to TCPEndPoint or UDPEndPoint for transport
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
        /// Cast to TCPEndPoint or UDPEndPoint for transport metadata; address-only traffic uses
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
                    _payloadLength = Transport?.PayloadLength ?? 0;
                    _cached |= Cached.PayloadLength;
                }
                return _payloadLength;
            }
        }

        /// <summary>
        /// Current transport payload, including decoded child packets. Materialized once
        /// on demand; consumers must not mutate the returned array.
        /// </summary>
        public byte[] PayloadData => _payloadData ??= Transport switch
        {
            { HasPayloadData: true } transport => transport.PayloadData,
            { PayloadPacket: { } payload } => payload.Bytes,
            _ => [],
        };

        // These cheap classifications reuse the cached layers and addresses. Preserve the
        // existing CaptureSummary semantics, including its IPv4 multicast interpretation.
        public bool IsIPUnicast => IP is not null && NDP is null && TargetAddress is { } target
            && !target.Equals(IPAddress.Broadcast) && !target.IsIPv6Multicast;

        public bool IsARPAnnouncement => ARP    is { Operation: ArpOperation.Request }
            && SourceAddress!.Equals(TargetAddress);
        public bool IsARPGratuitous => ARP      is { Operation: ArpOperation.Response }
            && SourceAddress!.Equals(TargetAddress);
        public bool IsARPProbe => ARP           is { Operation: ArpOperation.Request }
            && SourceAddress!.Equals(IPAddress.Any);

        public bool IsDuplicateAddressDetection => IsARPProbe
            || (NDP is NdpNeighborSolicitationPacket && SourceAddress!.Equals(IPAddress.IPv6Any));

        public EthernetCapture(EthernetPacket packet) : this()
        {
            Packet = packet ?? throw new ArgumentNullException(nameof(packet));
        }

        private T? GetPacket<T>(ref T? value, Cached flag) where T : Packet
        {
            if ((_cached & flag) == 0)
            {
                value = Packet.Extract<T>();
                _cached |= flag; // Remember an absent layer as well.
            }
            return value;
        }

        private IPEndPoint? CreateEndPoint(IPAddress? address, bool source)
        {
            if (address is null)
                return null;

            return Transport switch
            {
                TcpPacket tcp => new TCPEndPoint(address, source ? tcp.SourcePort : tcp.DestinationPort),
                UdpPacket udp => new UDPEndPoint(address, source ? udp.SourcePort : udp.DestinationPort),
                _ => new IPEndPoint(address, 0),
            };
        }

        public bool HasPacket<T>() where T : Packet => HasPacket<T>(out _);

        public bool HasPacket<T>([NotNullWhen(true)] out T? packet) where T : Packet
        {
            return (packet = FindPacket<T>()) != null;
        }

        /// <summary>
        /// Caches common layer lookups, including absence, without allocating a per-capture
        /// dictionary. Other types retain PacketDotNet's shallow packet-tree lookup semantics.
        /// Raw lookup is independent of the protocol guards used by the metadata properties.
        /// </summary>
        public T? FindPacket<T>() where T : Packet
        {
            if (typeof(T) == typeof(IPPacket)) return GetPacket(ref _ip, Cached.IP) as T;
            if (typeof(T) == typeof(ArpPacket)) return GetPacket(ref _arp, Cached.Arp) as T;
            if (typeof(T) == typeof(TransportPacket)) return GetPacket(ref _transport, Cached.Transport) as T;
            if (typeof(T) == typeof(TcpPacket)) return GetPacket(ref _tcp, Cached.Tcp) as T;
            if (typeof(T) == typeof(UdpPacket)) return GetPacket(ref _udp, Cached.Udp) as T;
            if (typeof(T) == typeof(NdpPacket)) return GetPacket(ref _ndp, Cached.Ndp) as T;
            if (typeof(T) == typeof(WakeOnLanPacket)) return GetPacket(ref _wakeOnLan, Cached.WakeOnLan) as T;

            return Packet.Extract<T>();
        }
    }
}
