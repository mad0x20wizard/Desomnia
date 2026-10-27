using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>
    /// Creates independent evaluation snapshots without tracking connections between calls.
    /// Input contains one direction only. UDP histories expose only the newest datagram.
    /// Override Parse for application-specific, multi-datagram protocols.
    /// </summary>
    public class DefaultFilterContextParser : IFilterContextParser
    {
        /// <summary>Maximum sequence window reconstructed per direction, including gaps.</summary>
        protected virtual int MaximumTcpPayloadLength => 1024 * 1024;

        /// <summary>Maximum payload-bearing segments inspected in one direction, including retransmissions.</summary>
        protected virtual int MaximumTcpSegments => 1024;

        private IFilterContext InterpretPacket(INetworkCapture capture, FlowDirection direction)
        {
            var context = ParsePacket(capture, direction);
            bool startsAtSyn = context is IIPServiceFilterContext { Protocol: IPProtocol.TCP }
                && capture.FindPacket<TcpPacket>()?.Synchronize == true;
            return Interpret(context, startsAtSyn);
        }

        public virtual IFilterContext Parse(ReadOnlySpan<INetworkCapture> captures, FlowDirection direction)
        {
            ValidateDirection(direction);
            if (captures.IsEmpty)
                throw new ArgumentException("At least one capture is required.", nameof(captures));
            if (captures.Length == 1)
                return InterpretPacket(captures[0], direction);

            // Address resolution can establish the endpoints before transport arrives.
            // Every subsequent capture must retain this source-to-target orientation.
            var sourceAddress = captures[0].SourceEndPoint?.Address;
            var targetAddress = captures[0].TargetEndPoint?.Address;
            IPServiceEndPoint? source = null, target = null;
            int latest = -1;
            for (int i = 0; i < captures.Length; i++)
            {
                var capture = captures[i];
                if (!Equals(capture.SourceEndPoint?.Address, sourceAddress)
                    || !Equals(capture.TargetEndPoint?.Address, targetAddress))
                    throw new ArgumentException("Captures must share the same source and target addresses and direction.", nameof(captures));

                if (capture.SourceEndPoint is not IPServiceEndPoint currentSource
                    || capture.TargetEndPoint is not IPServiceEndPoint currentTarget)
                    continue;

                if (source is null)
                    (source, target) = (currentSource, currentTarget);
                else if (!source.Equals(currentSource) || !target!.Equals(currentTarget))
                    throw new ArgumentException("Captures must describe exactly one transport connection.", nameof(captures));
                latest = i;
            }

            if (source is null || target is null)
                return InterpretPacket(captures[^1], direction);

            var peer = direction == FlowDirection.Inbound ? source : target;
            var watched = direction == FlowDirection.Inbound ? target : source;
            IPService service = watched;
            if (service.Protocol == IPProtocol.UDP)
            {
                // Never join independent datagrams, including empty ones.
                return InterpretPacket(captures[latest], direction);
            }

            var payload = TCPPayloadReassembly.Reconstruct(captures, source, target,
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

            if (!capture.HasPacket<IPPacket>())
                return new CaptureFilterContext(remote.Address) { Direction = direction };

            if (local is IPServiceEndPoint service && remote is IPServiceEndPoint)
                return capture.PayloadLength == 0
                    ? new ServiceCaptureFilterContext(remote, service) { Direction = direction }
                    : new PacketPayloadFilterContext(remote, service, capture, capture.PayloadLength) { Direction = direction };

            if (capture.HasPacket<NdpPacket>())
                return new CaptureFilterContext(remote.Address) { Direction = direction };

            var ip = capture.FindPacket<IPPacket>()!;
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
