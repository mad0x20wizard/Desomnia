using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture
{
    /// <summary>A bounded view of one direction, rebuilt entirely from the current history.</summary>
    internal static class TCPPayloadReassembly
    {
        internal readonly record struct Result(ReadOnlyMemory<byte>[] Parts, int Length, bool StartsAtSyn);
        private readonly record struct Segment(uint Sequence, byte[] Data);
        private readonly record struct Range(long Offset, ReadOnlyMemory<byte> Data)
        {
            public long End => Offset + Data.Length;
        }

        public static Result Reconstruct(ReadOnlySpan<INetworkCapture> captures, IPServiceEndPoint source,
            IPServiceEndPoint target, IPService service, int maximumLength, int maximumSegments)
        {
            if (maximumLength <= 0 || maximumSegments <= 0)
                throw new InvalidOperationException("TCP reconstruction limits must be positive.");

            List<Segment> segments = [];
            uint? start = null;
            foreach (var capture in captures)
            {
                if (!source.Equals(capture.SourceEndPoint) || !target.Equals(capture.TargetEndPoint))
                    continue;
                if (capture.FindPacket<TcpPacket>() is not { } tcp)
                    continue;

                uint sequence = unchecked(tcp.SequenceNumber + (tcp.Synchronize ? 1u : 0u));
                if (tcp.Synchronize)
                {
                    if (start is { } previous && sequence != previous)
                        throw Invalid("Different TCP SYN sequence numbers in one connection attempt.");
                    start = sequence;
                }
                if (capture.PayloadLength == 0)
                    continue;
                if (segments.Count >= maximumSegments || capture.PayloadLength > maximumLength)
                    throw Invalid("TCP reconstruction exceeds its configured capture or byte limit.");
                segments.Add(new(sequence, capture.PayloadData));
            }
            if (segments.Count == 0)
                return new([], 0, start.HasValue);

            // Signed modular differences support wraparound within a window smaller than 2^31.
            uint origin = start ?? segments[0].Sequence;
            var ranges = segments.Select(segment =>
                new Range(unchecked((int)(segment.Sequence - origin)), segment.Data)).ToList();
            ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            long first = start.HasValue ? 0 : ranges[0].Offset;
            if (ranges[0].Offset < first || ranges.Any(range => range.End - first > maximumLength))
                throw Invalid("TCP data falls outside the bounded sequence window.");

            // Keep a non-overlapping coverage map, including islands beyond gaps. Compare
            // each retransmission with that map, not with every earlier retransmission.
            // This keeps repeated identical segments from causing quadratic byte comparisons.
            List<Range> coverage = [];
            int firstOverlap = 0;
            foreach (var range in ranges)
            {
                while (firstOverlap < coverage.Count && coverage[firstOverlap].End <= range.Offset)
                    firstOverlap++;
                for (int i = firstOverlap; i < coverage.Count && coverage[i].Offset < range.End; i++)
                {
                    var previous = coverage[i];
                    long offset = Math.Max(previous.Offset, range.Offset);
                    int overlap = (int)(Math.Min(previous.End, range.End) - offset);
                    if (!previous.Data.Span.Slice((int)(offset - previous.Offset), overlap)
                        .SequenceEqual(range.Data.Span.Slice((int)(offset - range.Offset), overlap)))
                        throw Invalid("Conflicting TCP retransmission or overlapping payload.");
                }
                long tail = coverage.Count == 0 ? range.Offset : Math.Max(range.Offset, coverage[^1].End);
                if (range.End > tail)
                    coverage.Add(new(tail, range.Data[(int)(tail - range.Offset)..]));
            }

            List<ReadOnlyMemory<byte>> parts = [];
            long next = first;
            foreach (var range in coverage)
            {
                if (range.Offset > next)
                    break; // A later evaluation may fill the gap.
                if (range.End <= next)
                    continue;
                parts.Add(range.Data[(int)(next - range.Offset)..]);
                next = range.End;
            }
            return new(parts.ToArray(), (int)(next - first), start.HasValue);

            MalformedServicePayloadException Invalid(string message) => new(service, message);
        }
    }
}
