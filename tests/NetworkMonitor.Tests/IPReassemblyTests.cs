using Microsoft.Extensions.Logging.Abstractions;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter;
using Microsoft.Extensions.Configuration;
using PacketDotNet;
using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Text;
using Xunit;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Tests;

public class IPReassemblyTests
{
    private static ReassemblyOptions Options => new()
        { Protocol = ReassemblyProtocol.IPv4 | ReassemblyProtocol.IPv6, Timeout = TimeSpan.FromSeconds(30) };

    private static IPReassembly Create(bool ipv6, ReassemblyOptions? options = null, TimeProvider? clock = null,
        ILogger<IPReassembly>? logger = null)
        => ipv6 ? new IPv6Reassembly(options ?? Options, clock) { Logger = logger ?? NullLogger<IPReassembly>.Instance }
            : new IPv4Reassembly(options ?? Options, clock) { Logger = logger ?? NullLogger<IPReassembly>.Instance };

    private static long RetainedBytes(IPReassembly reassembly)
        => (long)typeof(IPReassembly).GetField("_retainedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reassembly)!;

    [Theory]
    [InlineData(false, false, 65535, true)]
    [InlineData(false, true, 65535, true)]
    [InlineData(true, false, 65527, true)]
    [InlineData(true, true, 65527, true)]
    [InlineData(true, false, 65535, false)]
    [InlineData(true, true, 65535, false)]
    public void Ipv6DatagramLimitUsesOnlyOffsetZeroHeaders(bool firstExtensions, bool laterExtensions, int length, bool accepted)
    {
        var data = Datagram(text: new string('x', length - 8));
        foreach (bool firstArrivesLast in new[] { false, true })
        {
            using var reassembly = Create(true);
            var first = Fragment(true, data, 0, 16, true, extensions: firstExtensions);
            if (!firstArrivesLast) Assert.Null(Consume(reassembly, first));
            INetworkCapture? capture = null;
            for (int offset = 16; offset < length; offset += 1024)
                capture = Consume(reassembly, Fragment(true, data, offset, Math.Min(1024, length - offset),
                    offset + 1024 < length, extensions: laterExtensions));
            if (firstArrivesLast) capture = Consume(reassembly, first);
            if (accepted)
            {
                Assert.NotNull(capture);
                Assert.Equal(length - 8, capture.PayloadLength);
                Assert.Equal(data[8..], capture.PayloadData);
                var ip = capture.FindPacket<IPv6Packet>()!;
                Assert.Equal(length + (firstExtensions ? 8 : 0), ip.Bytes.Length - 40);
            }
            else Assert.Null(capture);
            Assert.Equal(0, RetainedBytes(reassembly));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DefaultContextUsesFragmentMetadataAndDefersPayloadMaterialization(bool ipv6, bool tcp)
    {
        var data = Datagram(tcp);
        using var reassembly = Create(ipv6);
        int split = ipv6 && tcp ? 24 : 8;
        Consume(reassembly, Fragment(ipv6, data, 0, split, true, protocol: tcp ? (byte)6 : (byte)17));
        var capture = Consume(reassembly, Fragment(ipv6, data, split, data.Length - split, false, protocol: tcp ? (byte)6 : (byte)17))!;
        var parser = new DefaultFilterContextParser();
        foreach (var direction in new[] { FlowDirection.Inbound, FlowDirection.Outbound })
        {
            var context = Assert.IsAssignableFrom<IIPServicePayloadFilterContext>(parser.Parse([capture], direction));
            Assert.Same(direction == FlowDirection.Inbound ? capture.SourceEndPoint : capture.TargetEndPoint, context.RemoteEndPoint);
            Assert.Equal(tcp ? IPProtocol.TCP : IPProtocol.UDP, context.Protocol);
            Assert.Equal(data.Length - (tcp ? 20 : 8), context.PayloadLength);
            Assert.Null(typeof(EthernetCapture).GetField("_payloadData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
            if (!tcp)
                Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HttpHistoryCombinesIndividuallyFragmentedTcpSegments(bool ipv6)
    {
        const string text = "GET /several?q=1 HTTP/1.1\r\nHost: example.test\r\n\r\n";
        using var reassembly = Create(ipv6);
        var pipeline = new CapturePipeline([reassembly]) { Logger = NullLogger<CapturePipeline>.Instance };
        INetworkCapture Reassemble(uint sequence, string payload, bool syn, uint id)
        {
            var data = Datagram(tcp: true, text: payload);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), sequence);
            data[13] = syn ? (byte)2 : (byte)16;
            int split = ipv6 ? 24 : 8;
            Assert.Null(pipeline.TryCapturePacket(Fragment(ipv6, data, split, data.Length - split, false, id, protocol: 6)));
            return Assert.IsType<FragmentedIPCapture>(pipeline.TryCapturePacket(Fragment(ipv6, data, 0, split, true, id, protocol: 6)));
        }
        // Payload on the SYN anchors the stream. The following segment arrives before the gap filler.
        uint synSequence = uint.MaxValue - 5;
        uint start = unchecked(synSequence + 1);
        INetworkCapture[] history = [Reassemble(synSequence, text[..8], true, 1), Reassemble(unchecked(start + 20), text[20..], false, 2)];
        var parser = new HTTPFilterContextParser();
        var incomplete = parser.Parse(history, FlowDirection.Inbound);
        Assert.False(incomplete is IHTTPFilterContext);
        Assert.Equal(text[..8], Encoding.ASCII.GetString(((IIPServicePayloadFilterContext)incomplete).PayloadData));
        var middle = Reassemble(unchecked(start + 8), text[8..20], false, 3);
        var complete = Assert.IsAssignableFrom<IHTTPFilterContext>(parser.Parse([.. history, middle, middle], FlowDirection.Inbound));
        Assert.Equal("/several", complete.Path);
        Assert.Equal("1", complete.Query["q"].ToString());
        Assert.Equal(text, Encoding.ASCII.GetString(complete.PayloadData));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedByteBudgetRejectsOverflowAndRecoversAfterExpiry(bool ipv6)
    {
        const long limit = 16 * 1024 * 1024;
        var logger = new TraceLogger();
        var clock = new ManualClock();
        using var reassembly = Create(ipv6, clock: clock, logger: logger);
        var data = Datagram(text: new string('x', 59992));
        int wireLength = Fragment(ipv6, data, 0, data.Length, true).CapturedBytes.Length;
        uint count = (uint)(limit / wireLength);
        for (uint id = 0; id < count; id++)
            Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, data.Length, true, id)));
        long retained = RetainedBytes(reassembly);
        Assert.Equal(count * (long)wireLength, retained);
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, data.Length, true, count)));
        Assert.Equal(retained, RetainedBytes(reassembly));
        Assert.Contains(logger.Messages, message => message.Contains("Retained frame byte limit exceeded"));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(0, RetainedBytes(reassembly));
        var small = Datagram();
        Consume(reassembly, Fragment(ipv6, small, 0, 16, true, count));
        Assert.NotNull(Consume(reassembly, Fragment(ipv6, small, 16, small.Length - 16, false, count)));
        Assert.Equal(0, RetainedBytes(reassembly));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TraceReportsMalformedFramesOverlapAndExpiredCollections(bool ipv6)
    {
        var logger = new TraceLogger();
        var clock = new ManualClock();
        using var reassembly = Create(ipv6, clock: clock, logger: logger);
        var data = Datagram();
        var truncated = Fragment(ipv6, data, 0, 16, true).CapturedBytes[..^1];
        Consume(reassembly, (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, truncated));
        Consume(reassembly, Fragment(ipv6, data, 0, 16, true, 42));
        Consume(reassembly, Fragment(ipv6, data, 8, 16, true, 42));
        Consume(reassembly, Fragment(ipv6, data, 0, 16, true, 43));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Contains(logger.Messages, message => message.Contains("malformed"));
        Assert.Contains(logger.Messages, message => message.Contains("ID=42") && message.Contains("Overlapping"));
        Assert.Contains(logger.Messages, message => message.Contains("ID=43") && message.Contains("Expired") && message.Contains("1 fragments"));
        Assert.Equal(0, RetainedBytes(reassembly));
    }

    private sealed class TraceLogger : ILogger<IPReassembly>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel level) => level == LogLevel.Trace;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Trace, level);
            Messages.Add(formatter(state, exception));
        }
    }

    internal static byte[] Datagram(bool tcp = false, string text = "abcdefghijklmnopqrstuvwxyz0123456789")
    {
        var bytes = new byte[(tcp ? 20 : 8) + text.Length];
        Put16(bytes, 0, 50000); Put16(bytes, 2, 80);
        if (tcp) { bytes[12] = 0x50; bytes[13] = 0x18; }
        else Put16(bytes, 4, bytes.Length);
        Encoding.ASCII.GetBytes(text).CopyTo(bytes, tcp ? 20 : 8);
        return bytes;
    }

    internal static EthernetPacket Fragment(bool ipv6, byte[] datagram, int offset, int length, bool more,
        uint id = 42, byte protocol = 17, bool extensions = false, int vlan = 0)
    {
        int ethernet = vlan == 0 ? 14 : 18;
        int prefix = ipv6 ? (extensions ? 48 : 40) : 20;
        byte[] bytes = new byte[ethernet + prefix + (ipv6 ? 8 : 0) + length];
        FilterParserPackets.WatchedMac.GetAddressBytes().CopyTo(bytes, 0);
        FilterParserPackets.PeerMac.GetAddressBytes().CopyTo(bytes, 6);
        Put16(bytes, 12, vlan == 0 ? (ipv6 ? 0x86dd : 0x800) : 0x8100);
        if (vlan != 0) { Put16(bytes, 14, vlan); Put16(bytes, 16, ipv6 ? 0x86dd : 0x800); }
        int ip = ethernet;
        if (ipv6)
        {
            bytes[ip] = 0x60; Put16(bytes, ip + 4, bytes.Length - ethernet - 40);
            bytes[ip + 6] = extensions ? (byte)0 : (byte)44; bytes[ip + 7] = 64;
            IPAddress.Parse("2001:db8::10").GetAddressBytes().CopyTo(bytes, ip + 8);
            IPAddress.Parse("2001:db8::20").GetAddressBytes().CopyTo(bytes, ip + 24);
            if (extensions) bytes[ip + 40] = 44;
            int frag = ip + prefix;
            bytes[frag] = protocol; Put16(bytes, frag + 2, offset | (more ? 1 : 0));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(frag + 4), id);
        }
        else
        {
            bytes[ip] = 0x45; Put16(bytes, ip + 2, bytes.Length - ethernet);
            Put16(bytes, ip + 4, (int)id); Put16(bytes, ip + 6, offset / 8 | (more ? 0x2000 : 0));
            bytes[ip + 8] = 64; bytes[ip + 9] = protocol;
            FilterParserPackets.Peer.GetAddressBytes().CopyTo(bytes, ip + 12);
            FilterParserPackets.Watched.GetAddressBytes().CopyTo(bytes, ip + 16);
        }
        datagram.AsSpan(offset, length).CopyTo(bytes.AsSpan(bytes.Length - length));
        return (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, bytes);
    }

    private static void Put16(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), checked((ushort)value));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutOfOrderReassemblyIsLazyAndPreservesOriginalFrames(bool ipv6)
    {
        byte[] data = Datagram();
        var first = Fragment(ipv6, data, 0, 16, true);
        var last = Fragment(ipv6, data, 16, data.Length - 16, false);
        byte[] firstBytes = (byte[])first.HeaderSegment.Bytes.Clone(), lastBytes = (byte[])last.HeaderSegment.Bytes.Clone();
        using var reassembly = Create(ipv6);
        Assert.Null(Consume(reassembly, last));
        var result = Consume(reassembly, first);
        Assert.IsType<FragmentedIPCapture>(result);
        var capture = Assert.IsType<FragmentedIPCapture>(result);
        Assert.Equal(data[8..], capture.PayloadData);
        Assert.Same(capture.PayloadData, capture.PayloadData);
        Assert.Equal(data.Length - 8, capture.PayloadLength);
        Assert.Equal(50000, Assert.IsType<IPServiceEndPoint>(capture.SourceEndPoint).Port);
        Assert.Equal(80, Assert.IsType<IPServiceEndPoint>(capture.TargetEndPoint).Port);
        Assert.Equal(IPProtocol.UDP, ((IPServiceEndPoint)capture.SourceEndPoint!).Protocol);
        Assert.Equal(IPProtocol.UDP, ((IPServiceEndPoint)capture.TargetEndPoint!).Protocol);
        Assert.Same(capture.SourceAddress, capture.SourceEndPoint!.Address);
        Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.Equal(new[] { first, last }, capture.ToArray());
        var normalized = capture.FindPacket<IPPacket>();
        Assert.NotNull(normalized);
        Assert.False(normalized.IsIPFragment());
        Assert.Equal(data, normalized.PayloadPacket.Bytes);
        Assert.Same(normalized, capture.FindPacket<IPPacket>());
        Assert.Same(capture.FindPacket<UdpPacket>(), capture.FindPacket<UdpPacket>());
        Assert.Equal(data[8..], capture.FindPacket<UdpPacket>()!.PayloadData);
        Assert.Throws<NotSupportedException>(() => capture.FindPacket<EthernetPacket>());
        Assert.Equal(firstBytes, first.HeaderSegment.Bytes);
        Assert.Equal(lastBytes, last.HeaderSegment.Bytes);
        Assert.Equal(firstBytes, first.MakeCapturedCopy().CapturedBytes.ToArray());
        Assert.Equal(lastBytes, last.MakeCapturedCopy().CapturedBytes.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstFragmentSuppliesMetadataWithoutNormalizingOrDecodingTransport(bool ipv6)
    {
        var data = Datagram(tcp: true);
        int split = ipv6 ? 24 : 8; // The IPv4 TCP header is deliberately incomplete.
        var first = Fragment(ipv6, data, 0, split, true, protocol: 6);
        var last = Fragment(ipv6, data, split, data.Length - split, false, protocol: 6);
        byte[] before = (byte[])first.HeaderSegment.Bytes.Clone();
        using var reassembly = Create(ipv6);
        Assert.Null(Consume(reassembly, last));
        INetworkCapture capture = Assert.IsType<FragmentedIPCapture>(Consume(reassembly, first));

        // Interface and base-class calls must dispatch to the presence-only override.
        Assert.True(capture.HasPacket<IPPacket>());
        Assert.True(capture.HasPacket<TcpPacket>());
        Assert.True(((EthernetCapture)capture).HasPacket<TransportPacket>());
        Assert.True(((FragmentedIPCapture)capture).HasPacket<Packet>());
        Assert.Equal(!ipv6, capture.HasPacket<IPv4Packet>());
        Assert.Equal(ipv6, capture.HasPacket<IPv6Packet>());
        Assert.False(capture.HasPacket<UdpPacket>());
        Assert.False(capture.HasPacket<NdpPacket>());
        Assert.False(capture.HasPacket<NdpNeighborSolicitationPacket>());
        Assert.False(capture.HasPacket<IcmpV4Packet>());
        Assert.False(capture.HasPacket<IcmpV6Packet>());
        Assert.False(capture.HasPacket<ArpPacket>());
        Assert.False(capture.HasPacket<UdpPacket>(out var absentUdp));
        Assert.Null(absentUdp);
        Assert.False(capture.HasPacket<NdpPacket>(out var absentNdp));
        Assert.Null(absentNdp);
        Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));

        Assert.Equal(new EthernetCapture(first).Type, capture.Type);
        Assert.Equal(first.Type, capture.Type);
        Assert.Equal(FilterParserPackets.PeerMac, capture.SourcePhysicalAddress);
        Assert.Equal(ipv6 ? IPAddress.Parse("2001:db8::10") : FilterParserPackets.Peer, capture.SourceAddress);
        Assert.Equal(ipv6 ? IPAddress.Parse("2001:db8::20") : FilterParserPackets.Watched, capture.TargetAddress);
        Assert.True(capture.IsIPUnicast);
        Assert.Same(capture.SourceAddress, capture.SourceAddress);
        Assert.Same(capture.TargetAddress, capture.TargetAddress);
        Assert.Equal(50000, Assert.IsType<IPServiceEndPoint>(capture.SourceEndPoint).Port);
        Assert.Equal(80, Assert.IsType<IPServiceEndPoint>(capture.TargetEndPoint).Port);
        Assert.Equal(IPProtocol.TCP, ((IPServiceEndPoint)capture.SourceEndPoint!).Protocol);
        Assert.Equal(IPProtocol.TCP, ((IPServiceEndPoint)capture.TargetEndPoint!).Protocol);
        Assert.Same(capture.SourceAddress, capture.SourceEndPoint.Address);
        Assert.Same(capture.TargetAddress, capture.TargetEndPoint.Address);
        Assert.Equal(data.Length - 20, capture.PayloadLength);
        Assert.False(first.Extract<IPPacket>().IsPayloadInitialized);
        Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.Equal(before, first.HeaderSegment.Bytes);

        Assert.True(capture.HasPacket<TcpPacket>(out var tcp));
        Assert.Equal(data[20..], tcp.PayloadData);
        Assert.Same(tcp, capture.FindPacket<TcpPacket>());
        Assert.NotNull(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.Equal(before, first.HeaderSegment.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GapsAndExactDuplicatesDoNotCompleteOrDoubleCount(bool ipv6)
    {
        var data = Datagram();
        using var reassembly = Create(ipv6);
        var first = Fragment(ipv6, data, 0, 16, true);
        Assert.Null(Consume(reassembly, first));
        Assert.Null(Consume(reassembly, first));
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 32, data.Length - 32, false)));
        var result = Consume(reassembly, Fragment(ipv6, data, 16, 16, true));
        Assert.IsType<FragmentedIPCapture>(result);
        Assert.Equal(3, result!.Count());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConflictingDuplicatesAndPartialOverlapsPoisonTheWholeCollection(bool ipv6, bool exactRange)
    {
        var data = Datagram();
        using var reassembly = Create(ipv6);
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, 16, true)));
        var altered = (byte[])data.Clone(); altered[8] ^= 1;
        Assert.Null(Consume(reassembly, Fragment(ipv6, altered, exactRange ? 0 : 8, 16, true)));
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 16, data.Length - 16, false)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlineDoesNotSlideAndTimerReleasesIdleCollections(bool ipv6)
    {
        var clock = new ManualClock();
        using var reassembly = Create(ipv6, Options with { Timeout = TimeSpan.FromSeconds(10) }, clock);
        var data = Datagram(); var first = Fragment(ipv6, data, 0, 16, true);
        Consume(reassembly, first);
        clock.Advance(TimeSpan.FromSeconds(9));
        Consume(reassembly, first); // Retransmission must not restart the clock.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0L, typeof(IPReassembly).GetField("_retainedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reassembly));
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 16, data.Length - 16, false)));
        Assert.IsType<FragmentedIPCapture>(Consume(reassembly, first));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledFamilyRejectsOnlyItsFragments(bool ipv6)
    {
        using var disabled = Create(ipv6, Options with { Protocol = ReassemblyProtocol.None });
        Assert.Null(Consume(disabled, Fragment(ipv6, Datagram(), 0, 16, true)));
        Assert.False(disabled.TryReassemble(Fragment(!ipv6, Datagram(), 0, 16, true), out _));
        Assert.False(disabled.TryReassemble(FilterParserPackets.Udp("data").Single(), out _));
    }

    [Fact]
    public void IPv6AtomicFragmentsDoNotJoinOrReplacePendingCollection()
    {
        var data = Datagram(); using var reassembly = Create(true);
        var first = Fragment(true, data, 0, 16, true);
        Assert.Null(Consume(reassembly, first));
        var atomic = Consume(reassembly, Fragment(true, data, 0, data.Length, false));
        Assert.IsType<FragmentedIPCapture>(atomic);
        Assert.Single(atomic!);
        Assert.IsType<FragmentedIPCapture>(Consume(reassembly, Fragment(true, data, 16, data.Length - 16, false)));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(58)]
    public void IPv6RejectsIncompleteUpperHeadersAndFragmentedNeighborDiscovery(byte protocol)
    {
        using var reassembly = Create(true);
        var data = Datagram(tcp: protocol == 6);
        if (protocol == 58) data[0] = 135;
        Assert.Null(Consume(reassembly, Fragment(true, data, 0, 8, true, protocol: protocol)));
        if (protocol == 58)
            Assert.Null(Consume(reassembly, Fragment(true, data, 0, data.Length, false, protocol: protocol)));
    }

    [Fact]
    public void IPv6NormalizationRetainsExtensionsBeforeAndAfterFragmentHeader()
    {
        var datagram = Datagram(); byte[] data = new byte[8 + datagram.Length];
        data[0] = 17; datagram.CopyTo(data, 8); // Destination Options after the Fragment header.
        using var reassembly = Create(true);
        Consume(reassembly, Fragment(true, data, 0, 24, true, protocol: 60, extensions: true));
        var result = Consume(reassembly, Fragment(true, data, 24, data.Length - 24, false, protocol: 60, extensions: true));
        var capture = Assert.IsType<FragmentedIPCapture>(result);
        Assert.True(((INetworkCapture)capture).HasPacket<IPv6Packet>());
        Assert.True(capture.HasPacket<UdpPacket>());
        Assert.True(capture.HasPacket<TransportPacket>());
        Assert.False(capture.HasPacket<ArpPacket>(out var arp));
        Assert.Null(arp);
        Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.Equal(datagram[8..], capture.PayloadData);
        var ip = capture.FindPacket<IPv6Packet>()!;
        Assert.Equal(0, ip.Bytes[6]);
        Assert.Equal(60, ip.Bytes[40]);
        Assert.Equal(17, ip.Bytes[48]);
        Assert.Equal(datagram[8..], capture.FindPacket<UdpPacket>()!.PayloadData);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HttpParserConsumesReassembledCapture(bool ipv6)
    {
        var data = Datagram(tcp: true, text: "GET /fragmented?q=1 HTTP/1.1\r\nHost: example.test\r\n\r\n");
        int split = ipv6 ? 24 : 8; // IPv4 is allowed to split the TCP header itself.
        using var reassembly = Create(ipv6);
        Consume(reassembly, Fragment(ipv6, data, 0, split, true, protocol: 6));
        var result = Consume(reassembly, Fragment(ipv6, data, split, data.Length - split, false, protocol: 6));
        var capture = Assert.IsType<FragmentedIPCapture>(result);
        var context = Assert.IsAssignableFrom<IHTTPFilterContext>(new HTTPFilterContextParser().Parse([capture], FlowDirection.Inbound));
        Assert.Equal("/fragmented", context.Path);
        Assert.Equal("q=1", context.QueryString);
        Assert.Equal(data.Length - 20, context.PayloadLength);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PipelineRejectsTaggedFramesBeforeReassembly(bool ipv6, bool passthrough)
    {
        using var reassembly = Create(ipv6, Options with { Protocol = Options.Protocol | (passthrough ? ReassemblyProtocol.Passthrough : 0) });
        var unhandled = new Stub(false);
        var pipeline = new CapturePipeline([unhandled, reassembly]) { Logger = NullLogger<CapturePipeline>.Instance };
        var data = Datagram();
        foreach (var type in new ushort[] { 0x8100, 0x88a8 })
        foreach (bool fragmented in new[] { false, true })
        {
            var packet = Fragment(ipv6, data, 0, fragmented ? 16 : data.Length, fragmented, vlan: 17);
            packet.Type = (EthernetType)type;
            Assert.Null(pipeline.TryCapturePacket(packet));
            Assert.False(reassembly.TryReassemble(packet, out var capture));
            Assert.Null(capture);
        }
        Assert.Equal(0, unhandled.Calls);
        Assert.Equal(0L, RetainedBytes(reassembly));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsInvalidAlignmentAndTruncationAndBoundsPendingSets(bool ipv6)
    {
        var clock = new ManualClock();
        var data = Datagram(); using var reassembly = Create(ipv6, clock: clock);
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, 15, true)));
        var packet = Fragment(ipv6, data, 0, 16, true);
        var truncated = (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, packet.HeaderSegment.Bytes[..^1]);
        Assert.Null(Consume(reassembly, truncated));
        clock.Advance(TimeSpan.FromSeconds(60));
        for (uint id = 0; id < 1024; id++)
            Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, 16, true, id)));
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, 16, true, 1024)));
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, 16, true, 1024)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PipelineStopsAtFirstHandledResult(bool producesCapture)
    {
        var packet = FilterParserPackets.Udp("data").Single();
        INetworkCapture? expected = producesCapture ? new EthernetCapture(packet) : null;
        Stub skip = new(false), handled = new(true, expected), unreachable = new(true);
        var result = new CapturePipeline([skip, handled, unreachable]) { Logger = NullLogger<CapturePipeline>.Instance }.TryCapturePacket(packet);
        Assert.Same(expected, result);
        Assert.Equal(1, skip.Calls);
        Assert.Equal(1, handled.Calls);
        Assert.Equal(0, unreachable.Calls);
    }

    [Fact]
    public void PipelineFallsBackToRegularCapture()
    {
        var packet = FilterParserPackets.Udp("data").Single();
        Stub skip = new(false);
        var result = Assert.IsType<EthernetCapture>(new CapturePipeline([skip]) { Logger = NullLogger<CapturePipeline>.Instance }.TryCapturePacket(packet));
        Assert.Same(packet, Assert.Single(result));
        Assert.Equal(1, skip.Calls);
        Assert.IsType<EthernetCapture>(new CapturePipeline([]) { Logger = NullLogger<CapturePipeline>.Instance }.TryCapturePacket(packet));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CongestionMarkingIsPreservedAndNotEctMixIsRejected(bool ipv6, bool notEct)
    {
        var data = Datagram();
        var first = Fragment(ipv6, data, 0, 16, true);
        var last = Fragment(ipv6, data, 16, data.Length - 16, false);
        first.HeaderSegment.Bytes[15] = (byte)(notEct ? 0 : ipv6 ? 0x20 : 2);
        last.HeaderSegment.Bytes[15] = (byte)(ipv6 ? 0x30 : 3);
        using var reassembly = Create(ipv6);
        Consume(reassembly, first);
        var result = Consume(reassembly, last);
        if (notEct) Assert.Null(result);
        else Assert.IsType<FragmentedIPCapture>(result);
        if (!notEct)
        {
            var bytes = Assert.IsType<FragmentedIPCapture>(result).FindPacket<IPPacket>()!.Bytes;
            Assert.Equal(3, ipv6 ? (bytes[1] >> 4) & 3 : bytes[1] & 3);
        }
    }

    [Fact]
    public void InvalidFirstFragmentDiscardsAnAlreadyPendingIpv6Collection()
    {
        var data = Datagram(tcp: true);
        using var reassembly = Create(true);
        Consume(reassembly, Fragment(true, data, 24, data.Length - 24, false, protocol: 6));
        Assert.Null(Consume(reassembly, Fragment(true, data, 0, 8, true, protocol: 6)));
        Assert.Null(Consume(reassembly, Fragment(true, data, 0, 24, true, protocol: 6)));
    }

    [Fact]
    public void OrdinaryCaptureDoesNotAllocateInTheReassemblyPass()
    {
        using var ipv4 = Create(false); using var ipv6 = Create(true);
        var packet = FilterParserPackets.Tcp(100, "data").Single();
        for (int i = 0; i < 100; i++) { ipv4.TryReassemble(packet, out _); ipv6.TryReassemble(packet, out _); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
            if (ipv4.TryReassemble(packet, out _) || ipv6.TryReassemble(packet, out _)) throw new InvalidOperationException();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalReleasesCollectionsAndStopsProcessing(bool ipv6)
    {
        var clock = new ManualClock();
        var reassembly = Create(ipv6, clock: clock);
        var packet = Fragment(ipv6, Datagram(), 0, 16, true);
        Assert.Null(Consume(reassembly, packet));
        reassembly.Dispose();
        reassembly.Dispose();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0L, typeof(IPReassembly).GetField("_retainedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reassembly));
        Assert.Throws<ObjectDisposedException>(() => reassembly.TryReassemble(packet, out _));
    }

    private static INetworkCapture? Consume(IFragmentReassembly reassembly, EthernetPacket packet)
    {
        Assert.True(reassembly.TryReassemble(packet, out var capture));
        return capture;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void FamilyAndPassthroughFlagsSelectEachMode(int flags)
    {
        var options = Options with { Protocol = (ReassemblyProtocol)flags };
        using var ipv4 = Create(false, options);
        using var ipv6 = Create(true, options);
        var pipeline = new CapturePipeline([ipv4, ipv6]) { Logger = NullLogger<CapturePipeline>.Instance };
        foreach (bool v6 in new[] { false, true })
        {
            var data = Datagram();
            var first = Fragment(v6, data, 0, 16, true);
            var last = Fragment(v6, data, 16, data.Length - 16, false);
            bool selected = (flags & (v6 ? 2 : 1)) != 0;
            var a = pipeline.TryCapturePacket(first);
            var b = pipeline.TryCapturePacket(last);
            if (selected && (flags & 4) != 0)
            {
                Assert.Same(first, Assert.Single(Assert.IsType<EthernetCapture>(a)));
                Assert.Same(last, Assert.Single(Assert.IsType<EthernetCapture>(b)));
            }
            else
            {
                Assert.Null(a);
                if (selected) Assert.Equal(new[] { first, last }, Assert.IsType<FragmentedIPCapture>(b).ToArray());
                else Assert.Null(b);
            }
        }
        Assert.IsType<EthernetCapture>(pipeline.TryCapturePacket(FilterParserPackets.Udp("ordinary").Single()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PassthroughExposesOnlyTransportHeadersThatArePresent(bool ipv6, bool tcp)
    {
        var data = Datagram(tcp);
        int split = tcp ? 24 : 16;
        using var reassembly = Create(ipv6, Options with { Protocol = (ReassemblyProtocol)7 });
        var first = Fragment(ipv6, data, 0, split, true, protocol: tcp ? (byte)6 : (byte)17, extensions: ipv6);
        var last = Fragment(ipv6, data, split, data.Length - split, false, protocol: tcp ? (byte)6 : (byte)17, extensions: ipv6);
        var firstBytes = first.CapturedBytes.ToArray();
        var lastBytes = last.CapturedBytes.ToArray();
        var a = Assert.IsType<EthernetCapture>(Consume(reassembly, first));
        var b = Assert.IsType<EthernetCapture>(Consume(reassembly, last));
        Assert.Equal(50000, Assert.IsType<IPServiceEndPoint>(a.SourceEndPoint).Port);
        Assert.Equal(80, Assert.IsType<IPServiceEndPoint>(a.TargetEndPoint).Port);
        Assert.Equal(data[(tcp ? 20 : 8)..split], a.PayloadData);
        Assert.Equal(split - (tcp ? 20 : 8), a.PayloadLength);
        Assert.NotNull(a.FindPacket<TransportPacket>());
        Assert.Null(a.FindPacket<ArpPacket>());
        Assert.Equal(0, Assert.IsType<IPEndPoint>(b.SourceEndPoint).Port);
        Assert.Equal(0, Assert.IsType<IPEndPoint>(b.TargetEndPoint).Port);
        Assert.Null(b.FindPacket<TransportPacket>());
        Assert.Null(b.FindPacket<TcpPacket>());
        Assert.Null(b.FindPacket<UdpPacket>());
        Assert.Null(b.FindPacket<NdpPacket>());
        Assert.Equal(0, b.PayloadLength);
        Assert.Empty(b.PayloadData);
        Assert.True(b.IsIPUnicast);
        Assert.Equal(first.DestinationHardwareAddress, a.TargetPhysicalAddress);
        Assert.Equal(last.DestinationHardwareAddress, b.TargetPhysicalAddress);
        Assert.IsAssignableFrom<IIPServicePayloadFilterContext>(new DefaultFilterContextParser().Parse([a], FlowDirection.Inbound));
        Assert.IsAssignableFrom<IIPFilterContext>(new DefaultFilterContextParser().Parse([b], FlowDirection.Inbound));
        Assert.Equal(firstBytes, first.CapturedBytes);
        Assert.Equal(lastBytes, last.CapturedBytes);
    }

    [Fact]
    public void PassthroughIncompleteIPv4TransportHeaderHasAddressOnlyMetadata()
    {
        var packet = Fragment(false, Datagram(tcp: true), 0, 8, true, protocol: 6);
        using var reassembly = Create(false, Options with { Protocol = ReassemblyProtocol.IPv4 | ReassemblyProtocol.Passthrough });
        var capture = Assert.IsType<EthernetCapture>(Consume(reassembly, packet));
        Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.SourceEndPoint).Port);
        Assert.Equal(0, capture.PayloadLength);
        Assert.Empty(capture.PayloadData);
        Assert.Null(capture.FindPacket<TcpPacket>());
        Assert.Null(capture.FindPacket<ArpPacket>());
        Assert.Equal(packet.DestinationHardwareAddress, capture.TargetPhysicalAddress);
    }

    [Fact]
    public void IPv6PassthroughDoesNotDecodePostFragmentExtensionsInLaterFragments()
    {
        var datagram = Datagram(text: "0123456789");
        byte[] data = new byte[8 + datagram.Length];
        data[0] = 17;
        datagram.CopyTo(data, 8);
        var first = Fragment(true, data, 0, 24, true, protocol: 60);
        var last = Fragment(true, data, 24, 2, false, protocol: 60);
        using var reassembly = Create(true, Options with { Protocol = ReassemblyProtocol.IPv6 | ReassemblyProtocol.Passthrough });
        var a = Assert.IsType<EthernetCapture>(Consume(reassembly, first));
        var b = Assert.IsType<EthernetCapture>(Consume(reassembly, last));
        Assert.Equal(80, Assert.IsType<IPServiceEndPoint>(a.TargetEndPoint).Port);
        Assert.Equal(0, Assert.IsType<IPEndPoint>(b.TargetEndPoint).Port);
        Assert.Null(b.FindPacket<TransportPacket>());
        Assert.Empty(b.PayloadData);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationPacketLookupUsesTheCompleteDatagram(bool ipv6)
    {
        var magic = new WakeOnLanPacket(FilterParserPackets.WatchedMac).Bytes;
        var data = new byte[8 + magic.Length];
        Put16(data, 0, 50000); Put16(data, 2, 9); Put16(data, 4, data.Length);
        magic.CopyTo(data, 8);
        using var reassembly = Create(ipv6);
        Assert.Null(Consume(reassembly, Fragment(ipv6, data, 0, 16, true)));
        var capture = Assert.IsType<FragmentedIPCapture>(Consume(reassembly,
            Fragment(ipv6, data, 16, data.Length - 16, false)));
        Assert.True(capture.HasPacket<UdpPacket>());
        Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.True(((INetworkCapture)capture).HasPacket<WakeOnLanPacket>());
        Assert.NotNull(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.True(capture.HasPacket<WakeOnLanPacket>(out var wol));
        Assert.Same(wol, capture.FindPacket<WakeOnLanPacket>());
        Assert.Equal(FilterParserPackets.WatchedMac, capture.FindPacket<WakeOnLanPacket>()!.DestinationAddress);
        Assert.Equal(FilterParserPackets.WatchedMac, capture.TargetPhysicalAddress);
    }

    [Fact]
    public void PresenceChecksInspectTheCompletePayloadForEncapsulatedPackets()
    {
        var inner = new IPv6Packet(IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2"))
        {
            PayloadPacket = new TcpPacket(50000, 80) { PayloadData = [1, 2, 3] }
        }.Bytes;
        byte[] data = new byte[8 + inner.Length];
        Put16(data, 0, 50000); Put16(data, 2, 3544); Put16(data, 4, data.Length); // Teredo
        inner.CopyTo(data, 8);
        using var reassembly = Create(false);
        Assert.Null(Consume(reassembly, Fragment(false, data, 0, 16, true)));
        INetworkCapture capture = Assert.IsType<FragmentedIPCapture>(Consume(reassembly,
            Fragment(false, data, 16, data.Length - 16, false)));

        Assert.True(capture.HasPacket<IPv4Packet>());
        Assert.True(capture.HasPacket<UdpPacket>());
        Assert.Null(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.True(capture.HasPacket<IPv6Packet>());
        Assert.NotNull(typeof(FragmentedIPCapture).GetField("_reassembled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture));
        Assert.True(capture.HasPacket<TcpPacket>(out var tcp));
        Assert.Equal(new byte[] { 1, 2, 3 }, tcp.PayloadData);
    }

    private sealed class Stub(bool handled, INetworkCapture? capture = null) : IFragmentReassembly
    {
        public int Calls;
        public bool TryReassemble(EthernetPacket packet, out INetworkCapture? result)
        {
            Calls++;
            result = capture;
            return handled;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private Action? _callback;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan time) { _ticks += time.Ticks; _callback?.Invoke(); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = () => callback(state);
            return new TimerHandle(() => _callback = null);
        }
        private sealed class TimerHandle(Action dispose) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => dispose();
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
