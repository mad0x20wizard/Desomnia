using Autofac;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;
using System.Net;
using System.Text;
using Xunit;
using static MadWizard.Desomnia.Network.Tests.FilterParserPackets;

namespace MadWizard.Desomnia.Network.Tests;

public class DefaultFilterContextParserTests
{
    private readonly DefaultFilterContextParser _parser = new();
    private static IIPServicePayloadFilterContext Payload(IFilterContext context)
        => Assert.IsAssignableFrom<IIPServicePayloadFilterContext>(context);
    private static string Text(IFilterContext context) => Encoding.Latin1.GetString(Payload(context).PayloadData);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SinglePacketNormalizesEndpointsAndOwnsFreshSnapshot(bool outbound, bool ipv6)
    {
        var capture = Tcp(100, "payload", outbound: outbound, ipv6: ipv6);
        var direction = outbound ? FlowDirection.Outbound : FlowDirection.Inbound;
        var context = Payload(_parser.Parse(capture, direction));
        Assert.Equal(direction, context.Direction);
        Assert.Equal(new IPService(IPProtocol.TCP, 80), context.Service);
        Assert.Same(outbound ? capture.TargetEndPoint : capture.SourceEndPoint, context.RemoteEndPoint);
        Assert.Same(context.RemoteEndPoint.Address, context.RemoteAddress);
        Assert.Equal(7, context.PayloadLength);
        Assert.NotSame(context, _parser.Parse(capture, direction));
        capture = Tcp(200, "another packet");
        Assert.Equal("payload", Text(context));
        Assert.Same(context.PayloadData, context.PayloadData);
    }

    [Fact]
    public void SynOffersServiceButNoPayloadCapability()
    {
        var capture = Tcp(100, syn: true);
        var context = _parser.Parse(capture, FlowDirection.Inbound);
        Assert.IsAssignableFrom<IIPServiceFilterContext>(context);
        Assert.False(context is IIPServicePayloadFilterContext);
    }

    [Fact]
    public void AddressResolutionIsNotAnIpContext()
    {
        var arp = Arp();
        var inbound = _parser.Parse(arp, FlowDirection.Inbound);
        var outbound = _parser.Parse(arp, FlowDirection.Outbound);
        Assert.False(inbound is IIPFilterContext);
        Assert.Same(arp.SourceEndPoint!.Address, inbound.RemoteAddress);
        Assert.Same(arp.TargetEndPoint!.Address, outbound.RemoteAddress);

        var ip = new IPv6Packet(IPAddress.Parse("2001:db8::10"), IPAddress.Parse("ff02::1:ff00:20"))
            .WithNDPNeighborSolicitation(IPAddress.Parse("2001:db8::20"), PeerMac);
        var ndp = new EthernetCapture(new EthernetPacket(PeerMac, WatchedMac, EthernetType.IPv6) { PayloadPacket = ip }.MakeCopy());
        Assert.False(_parser.Parse(ndp, FlowDirection.Inbound) is IIPFilterContext);
        Assert.Equal(IPAddress.Parse("2001:db8::20"), _parser.Parse(ndp, FlowDirection.Outbound).RemoteAddress);
    }

    [Theory]
    [InlineData(false, false, FlowDirection.Inbound, ICMPType.Echo)]
    [InlineData(false, true, FlowDirection.Outbound, ICMPType.Echo)]
    [InlineData(false, false, FlowDirection.Outbound, ICMPType.Unknown)]
    [InlineData(false, true, FlowDirection.Inbound, ICMPType.Unknown)]
    [InlineData(true, false, FlowDirection.Inbound, ICMPType.Echo)]
    [InlineData(true, true, FlowDirection.Outbound, ICMPType.Echo)]
    [InlineData(true, false, FlowDirection.Outbound, ICMPType.Unknown)]
    [InlineData(true, true, FlowDirection.Inbound, ICMPType.Unknown)]
    public void EchoIsNormalizedForBothIpVersions(bool ipv6, bool reply, FlowDirection direction, ICMPType expected)
    {
        IPPacket ip = ipv6
            ? new IPv6Packet(IPAddress.Parse("2001:db8::10"), IPAddress.Parse("2001:db8::20"))
            : new IPv4Packet(Peer, Watched);
        ip.Protocol = ipv6 ? ProtocolType.IcmpV6 : ProtocolType.Icmp;
        ip.PayloadData = [ipv6 ? (reply ? (byte)129 : (byte)128) : (reply ? (byte)0 : (byte)8), 0, 0, 0, 0, 0, 0, 0];
        ip.PayloadLength = 8;
        var capture = new EthernetCapture(new EthernetPacket(PeerMac, WatchedMac,
            ipv6 ? EthernetType.IPv6 : EthernetType.IPv4) { PayloadPacket = ip }.MakeCopy());
        Assert.Equal(expected, Assert.IsAssignableFrom<IICMPFilterContext>(
            _parser.Parse(capture, direction)).Type);
    }

    [Fact]
    public void OtherIpProtocolsOfferOnlyIpMetadata()
    {
        var ip = new IPv4Packet(Peer, Watched) { Protocol = ProtocolType.Gre };
        var capture = new EthernetCapture(new EthernetPacket(PeerMac, WatchedMac, EthernetType.IPv4) { PayloadPacket = ip });
        var context = _parser.Parse(capture, FlowDirection.Inbound);
        Assert.IsAssignableFrom<IIPFilterContext>(context);
        Assert.False(context is IIPServiceFilterContext);
    }

    [Fact]
    public void ReassemblyOrdersSegmentsHandlesOverlapAndKeepsDirectionsSeparate()
    {
        INetworkCapture[] history =
        [
            Arp(), Tcp(100, syn: true), Tcp(900, syn: true, outbound: true),
            Tcp(104, "def"), Tcp(101, "abcd"), Tcp(101, "abcd"), Tcp(901, "response", outbound: true),
        ];
        Assert.Equal("abcdef", Text(_parser.Parse(history, FlowDirection.Inbound)));
        var response = Payload(_parser.Parse(history, FlowDirection.Outbound));
        Assert.Equal("response", Text(response));
        Assert.Equal(Peer, response.RemoteAddress);
        Assert.Equal(80, response.Service.Port);
        Assert.Equal(50000, response.RemoteEndPoint.Port);
        Assert.Equal("abcd", Text(_parser.Parse(history[4], FlowDirection.Inbound)));
    }

    [Fact]
    public void FirstTransportMayBeReplyAfterAddressResolution()
    {
        INetworkCapture[] history = [Arp(), Tcp(900, "reply", outbound: true), Tcp(100, "request")];
        Assert.Equal("request", Text(_parser.Parse(history, FlowDirection.Inbound)));
        Assert.Equal("reply", Text(_parser.Parse(history, FlowDirection.Outbound)));
    }

    [Fact]
    public void AddressResolutionCanProgressToIcmpWithoutTransportMetadata()
    {
        var ip = new IPv4Packet(Peer, Watched) { Protocol = ProtocolType.Icmp, PayloadData = [8, 0, 0, 0, 0, 0, 0, 0] };
        ip.PayloadLength = 8;
        var ping = new EthernetCapture(new EthernetPacket(PeerMac, WatchedMac, EthernetType.IPv4) { PayloadPacket = ip }.MakeCopy());
        INetworkCapture[] history = [Arp(), ping];
        var context = Assert.IsAssignableFrom<IICMPFilterContext>(_parser.Parse(history, FlowDirection.Inbound));
        Assert.Equal(ICMPType.Echo, context.Type);
        Assert.Equal(Peer, context.RemoteAddress);
        var absentReply = _parser.Parse(history, FlowDirection.Outbound);
        Assert.Equal(Peer, absentReply.RemoteAddress);
        Assert.False(absentReply is IICMPFilterContext);
    }

    [Fact]
    public void GapHoldsBackBytesUntilFilledAndOldSnapshotStaysUnchanged()
    {
        INetworkCapture[] history = [Tcp(100, syn: true), Tcp(101, "abc"), Tcp(107, "ghi"), Tcp(104, "def")];
        var earlier = _parser.Parse(history.AsSpan(0, 3), FlowDirection.Inbound);
        Assert.Equal("abc", Text(earlier));
        Assert.Equal("abcdefghi", Text(_parser.Parse(history, FlowDirection.Inbound)));
        Assert.Equal("abc", Text(earlier));
        history[1] = Tcp(104, "def");
        Assert.False(_parser.Parse(history.AsSpan(0, 3), FlowDirection.Inbound) is IIPServicePayloadFilterContext);
    }

    [Fact]
    public void ReconstructionHandlesWraparoundAndDataOnSyn()
    {
        INetworkCapture[] history = [Tcp(uint.MaxValue - 1, "ab", syn: true), Tcp(1, "cd")];
        Assert.Equal("abcd", Text(_parser.Parse(history, FlowDirection.Inbound)));
    }

    [Fact]
    public void WithoutSynEarliestObservedSequenceIsUsedAcrossWraparound()
    {
        INetworkCapture[] history = [Tcp(1, "cd"), Tcp(uint.MaxValue, "ab")];
        Assert.Equal("abcd", Text(_parser.Parse(history, FlowDirection.Inbound)));
    }

    [Fact]
    public void ConflictingOverlapsAreTerminalEvenBeyondAGap()
    {
        INetworkCapture[] history = [Tcp(100, syn: true), Tcp(110, "abc"), Tcp(111, "Xd")];
        var exception = Assert.Throws<MalformedServicePayloadException>(() => _parser.Parse(history, FlowDirection.Inbound));
        Assert.Equal(new IPService(IPProtocol.TCP, 80), exception.Service);
    }

    [Fact]
    public void NestedAndPartiallyOverlappingRetransmissionsUseConsistentCoverage()
    {
        INetworkCapture[] history = [Tcp(100, "abcdef"), Tcp(101, "bc"), Tcp(102, "cdefgh"), Tcp(103, "de")];
        Assert.Equal("abcdefgh", Text(_parser.Parse(history, FlowDirection.Inbound)));
        history[3] = Tcp(103, "dX");
        Assert.Throws<MalformedServicePayloadException>(() => _parser.Parse(history, FlowDirection.Inbound));
    }

    [Fact]
    public void ConflictingSynAndDifferentConnectionsAreRejected()
    {
        INetworkCapture[] history = [Tcp(100, syn: true), Tcp(200, syn: true)];
        Assert.Throws<MalformedServicePayloadException>(() => _parser.Parse(history, FlowDirection.Inbound));
        history[1] = Tcp(101, "abc", servicePort: 81);
        Assert.Throws<ArgumentException>(() => _parser.Parse(history, FlowDirection.Inbound));
        history[1] = Udp("abc");
        Assert.Throws<ArgumentException>(() => _parser.Parse(history, FlowDirection.Inbound));
    }

    [Fact]
    public void UdpUsesOnlyLatestDatagramInEachDirectionIncludingEmptyDatagrams()
    {
        INetworkCapture[] history = [Arp(), Udp("first"), Udp("second"), Udp("reply", outbound: true)];
        Assert.Equal("second", Text(_parser.Parse(history, FlowDirection.Inbound)));
        Assert.Equal("reply", Text(_parser.Parse(history, FlowDirection.Outbound)));
        history[2] = Udp("");
        Assert.False(_parser.Parse(history, FlowDirection.Inbound) is IIPServicePayloadFilterContext);
    }

    [Fact]
    public void MissingDirectionDoesNotBorrowOppositePayload()
    {
        INetworkCapture[] history = [Tcp(100, "request")];
        var context = _parser.Parse(history, FlowDirection.Outbound);
        Assert.False(context is IIPServicePayloadFilterContext);
        Assert.Equal(Peer, context.RemoteAddress);
        Assert.Equal(80, Assert.IsAssignableFrom<IIPServiceFilterContext>(context).Service.Port);
    }

    private sealed class SmallParser : DefaultFilterContextParser
    {
        protected override int MaximumTcpPayloadLength => 8;
        protected override int MaximumTcpSegments => 2;
    }

    [Fact]
    public void ReassemblyLimitsIncludeGapsAndRetransmissions()
    {
        var parser = new SmallParser();
        INetworkCapture[] sparse = [Tcp(100, syn: true), Tcp(109, "x")];
        Assert.Throws<MalformedServicePayloadException>(() => parser.Parse(sparse, FlowDirection.Inbound));
        INetworkCapture[] repeated = [Tcp(100, "x"), Tcp(100, "x"), Tcp(100, "x")];
        Assert.Throws<MalformedServicePayloadException>(() => parser.Parse(repeated, FlowDirection.Inbound));
    }

    private sealed class ObservedPayload : Packet
    {
        public int Reads { get; set; }
        public ObservedPayload() { Header = new(new byte[3]); }
        public override byte[] Bytes { get { Reads++; return base.Bytes; } }
    }

    [Fact]
    public void PayloadLengthDoesNotSerializeDecodedChildPacket()
    {
        var payload = new ObservedPayload();
        var transport = new UdpPacket(50000, 80) { PayloadPacket = payload };
        var ip = new IPv4Packet(Peer, Watched) { PayloadPacket = transport };
        var capture = new EthernetCapture(new EthernetPacket(PeerMac, WatchedMac, EthernetType.IPv4) { PayloadPacket = ip });
        payload.Reads = 0; // PacketDotNet can serialize while assigning parent/child relationships.
        var context = Payload(_parser.Parse(capture, FlowDirection.Inbound));
        Assert.Equal(3, context.PayloadLength);
        Assert.Equal(0, payload.Reads);
        Assert.Equal(3, context.PayloadData.Length);
        Assert.Same(context.PayloadData, context.PayloadData);
        Assert.Equal(1, payload.Reads);
    }

    [Fact]
    public void InvalidInputContractsAreExplicit()
    {
        Assert.Throws<ArgumentException>(() => _parser.Parse(Array.Empty<EthernetCapture>(), FlowDirection.Inbound));
        var capture = Tcp(100);
        Assert.Throws<ArgumentOutOfRangeException>(() => _parser.Parse(capture, (FlowDirection)99));
        var fragment = new IPv4Packet(Peer, Watched) { FragmentFlags = 1, PayloadPacket = new TcpPacket(50000, 80) };
        capture = new(new EthernetPacket(PeerMac, WatchedMac, EthernetType.IPv4) { PayloadPacket = fragment });
        Assert.IsAssignableFrom<IIPServiceFilterContext>(_parser.Parse(capture, FlowDirection.Inbound));
        capture = new(new EthernetPacket(PeerMac, WatchedMac, EthernetType.WakeOnLan) { PayloadPacket = new WakeOnLanPacket(WatchedMac) });
        Assert.Throws<NotSupportedException>(() => _parser.Parse(capture, FlowDirection.Inbound));
    }

    private sealed class RegistrationModule : Module
    {
        public void Register(ContainerBuilder builder) => Load(builder, new ModuleConfig<NetworkMonitorConfig>());
    }

    [Fact]
    public void ModuleDefaultAndNestedServiceOverrideResolveThroughRequestScope()
    {
        var builder = new ContainerBuilder();
        new RegistrationModule().Register(builder);
        using var root = builder.Build();
        using var host = root.BeginLifetimeScope();
        using var service = host.BeginLifetimeScope(b => b.RegisterType<HTTPFilterContextParser>().As<IFilterContextParser>().SingleInstance());
        using var request = service.BeginLifetimeScope();
        using var sibling = host.BeginLifetimeScope();
        Assert.IsType<DefaultFilterContextParser>(host.Resolve<IFilterContextParser>());
        Assert.IsType<HTTPFilterContextParser>(request.Resolve<IFilterContextParser>());
        Assert.Same(root.Resolve<IFilterContextParser>(), sibling.Resolve<IFilterContextParser>());
    }
}
