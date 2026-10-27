using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>
    /// Creates independent evaluation snapshots without tracking connections between calls.
    /// UDP histories expose only the newest datagram in the requested direction.
    /// Override the history overload for application-specific, multi-datagram protocols.
    /// </summary>
    public class DefaultFilterContextParser : IFilterContextParser
    {
        /// <summary>Maximum sequence window reconstructed per direction, including gaps.</summary>
        protected virtual int MaximumTcpPayloadLength => 1024 * 1024;

        /// <summary>Maximum payload-bearing segments inspected in one direction, including retransmissions.</summary>
        protected virtual int MaximumTcpSegments => 1024;

        public virtual IFilterContext Parse(INetworkCapture capture, FlowDirection direction)
        {
            ValidateDirection(direction);
            var context = ParsePacket(capture, direction);
            return Interpret(context, capture.FindPacket<TcpPacket>()?.Synchronize == true);
        }

        public virtual IFilterContext Parse(ReadOnlySpan<INetworkCapture> captures, FlowDirection direction)
        {
            ValidateDirection(direction);
            if (captures.IsEmpty)
                throw new ArgumentException("A demand history must contain at least one capture.", nameof(captures));

            // The inbound trigger may be ARP/NDP. Its addresses establish the sides even
            // when the first transport packet is a reply.
            var peerAddress = captures[0].SourceEndPoint?.Address;
            var watchedAddress = captures[0].TargetEndPoint?.Address;
            IPServiceEndPoint? peer = null, watched = null;
            int latest = -1, latestNonTransport = -1;
            for (int i = 0; i < captures.Length; i++)
            {
                var capture = captures[i];
                if (capture.SourceEndPoint is not IPServiceEndPoint source
                    || capture.TargetEndPoint is not IPServiceEndPoint target)
                {
                    bool sameDirection = direction == FlowDirection.Inbound
                        ? Equals(capture.SourceEndPoint?.Address, peerAddress) && Equals(capture.TargetEndPoint?.Address, watchedAddress)
                        : Equals(capture.SourceEndPoint?.Address, watchedAddress) && Equals(capture.TargetEndPoint?.Address, peerAddress);
                    if (sameDirection)
                        latestNonTransport = i;
                    continue;
                }

                if (peer is null)
                {
                    if (source.Address.Equals(peerAddress) && target.Address.Equals(watchedAddress))
                        (peer, watched) = (source, target);
                    else if (source.Address.Equals(watchedAddress) && target.Address.Equals(peerAddress))
                        (peer, watched) = (target, source);
                    else
                        throw new ArgumentException("Transport endpoints do not belong to the demand trigger.", nameof(captures));
                }

                bool inbound = source.Equals(peer) && target.Equals(watched);
                bool outbound = source.Equals(watched) && target.Equals(peer);
                if (!inbound && !outbound)
                    throw new ArgumentException("A demand history must describe exactly one transport connection.", nameof(captures));
                if (direction == FlowDirection.Inbound ? inbound : outbound)
                    latest = i;
            }

            if (peer is null || watched is null)
                return latestNonTransport >= 0
                    ? Parse(captures[latestNonTransport], direction)
                    : new CaptureFilterContext(peerAddress
                        ?? throw new NotSupportedException("A filter context requires a peer IP address.")) { Direction = direction };

            IPService service = watched;
            if (service.Protocol == IPProtocol.UDP)
            {
                // Never join independent datagrams, including empty ones.
                return latest < 0
                    ? new ServiceCaptureFilterContext(peer, service) { Direction = direction }
                    : Parse(captures[latest], direction);
            }

            var payload = TcpPayloadReassembly.Reconstruct(captures,
                direction == FlowDirection.Inbound ? peer : watched,
                direction == FlowDirection.Inbound ? watched : peer,
                service, MaximumTcpPayloadLength, MaximumTcpSegments);
            IFilterContext context = payload.Length == 0
                ? new ServiceCaptureFilterContext(peer, service) { Direction = direction }
                : new StreamPayloadFilterContext(peer, service, payload.Parts, payload.Length) { Direction = direction };
            return Interpret(context, payload.StartsAtSyn);
        }

        /// <summary>
        /// Specialize a truthful default context. A SYN anchors the TCP stream's beginning;
        /// without it, bytes start at the earliest observed sequence and may be a midstream fragment.
        /// </summary>
        protected virtual IFilterContext Interpret(IFilterContext context, bool startsAtSyn) => context;

        private static IFilterContext ParsePacket(INetworkCapture capture, FlowDirection direction)
        {
            var remote = direction == FlowDirection.Inbound ? capture.SourceEndPoint : capture.TargetEndPoint;
            var local = direction == FlowDirection.Inbound ? capture.TargetEndPoint : capture.SourceEndPoint;
            if (remote is null)
                throw new NotSupportedException("A filter context requires an IP address; Ethernet-only traffic has none.");

            if (capture.FindPacket<IPPacket>() is not { } ip || capture.HasPacket<NdpPacket>())
                return new CaptureFilterContext(remote.Address) { Direction = direction };

            if (local is IPServiceEndPoint service && remote is IPServiceEndPoint
                && capture.FindPacket<TransportPacket>() is { } transport)
                return capture.PayloadLength == 0
                    ? new ServiceCaptureFilterContext(remote, service) { Direction = direction }
                    : new PacketPayloadFilterContext(remote, service, capture, capture.PayloadLength) { Direction = direction };

            if (ip.Protocol == ProtocolType.Icmp && capture.FindPacket<IcmpV4Packet>() is { } icmp4)
                return new ICMPCaptureFilterContext(remote.Address,
                    icmp4.TypeCode == (direction == FlowDirection.Inbound ? IcmpV4TypeCode.EchoRequest : IcmpV4TypeCode.EchoReply)
                        ? ICMPType.Echo : ICMPType.Unknown) { Direction = direction };
            if (ip.Protocol == ProtocolType.IcmpV6 && capture.FindPacket<IcmpV6Packet>() is { } icmp6)
                return new ICMPCaptureFilterContext(remote.Address,
                    icmp6.Type == (direction == FlowDirection.Inbound ? IcmpV6Type.EchoRequest : IcmpV6Type.EchoReply)
                        ? ICMPType.Echo : ICMPType.Unknown) { Direction = direction };
            return new IPCaptureFilterContext(remote.Address) { Direction = direction };
        }

        private static void ValidateDirection(FlowDirection direction)
        {
            if (direction is not FlowDirection.Inbound and not FlowDirection.Outbound)
                throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }
}
