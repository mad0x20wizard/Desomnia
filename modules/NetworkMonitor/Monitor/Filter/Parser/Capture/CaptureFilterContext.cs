using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter
{
    // Snapshots retain immutable metadata/packet references, never an EthernetCapture copy
    // or a borrowed span. Payload arrays are owned by the snapshot or the capture cache.
    internal class CaptureFilterContext(IPAddress remote) : IFilterContext
    {
        public required FlowDirection Direction { get; init; }

        public IPAddress RemoteAddress { get; } = remote;
    }

    internal class IPCaptureFilterContext(IPAddress remote) : CaptureFilterContext(remote), IIPFilterContext
    {

    }

    internal sealed class ICMPCaptureFilterContext : IPCaptureFilterContext, IICMPFilterContext
    {
        public ICMPType Type { get; }

        public ICMPCaptureFilterContext(IPAddress remote, ICMPType type) : base(remote)
        {
            Type = type;
        }
    }

    internal class ServiceCaptureFilterContext : IPCaptureFilterContext, IIPServiceFilterContext
    {
        public IPEndPoint RemoteEndPoint { get; }

        public IPService Service { get; }

        public ServiceCaptureFilterContext(IPEndPoint remote, IPService service) : base(remote.Address)
        {
            RemoteEndPoint = remote;
            Service = service;
        }
    }

    internal sealed class PacketPayloadFilterContext : ServiceCaptureFilterContext, IIPServicePayloadFilterContext
    {
        private readonly TransportPacket _packet;
        private byte[]? _data;

        public int PayloadLength { get; }

        public byte[] PayloadData => _data ??= _packet.HasPayloadData ? _packet.PayloadData : _packet.PayloadPacket.Bytes;

        public PacketPayloadFilterContext(IPEndPoint remote,
            IPService service, TransportPacket packet, int length)
            : base(remote, service)
        {
            _packet = packet;
            PayloadLength = length;
        }
    }

    internal sealed class StreamPayloadFilterContext : ServiceCaptureFilterContext, IIPServicePayloadFilterContext
    {
        private readonly ReadOnlyMemory<byte>[] _parts;
        private byte[]? _data;

        public int PayloadLength { get; }

        public byte[] PayloadData
        {
            get
            {
                if (_data is not null)
                    return _data;

                var data = new byte[PayloadLength];
                int offset = 0;
                foreach (var part in _parts)
                {
                    part.Span.CopyTo(data.AsSpan(offset));
                    offset += part.Length;
                }
                return _data = data;
            }
        }

        public StreamPayloadFilterContext(IPEndPoint remote,
            IPService service, ReadOnlyMemory<byte>[] parts, int length)
            : base(remote, service)
        {
            _parts = parts;
            PayloadLength = length;
        }
    }
}
