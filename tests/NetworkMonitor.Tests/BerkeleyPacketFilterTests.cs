using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Device.Filter;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class BerkeleyPacketFilterTests
{
    private const ReassemblyProtocol Both = ReassemblyProtocol.IPv4 | ReassemblyProtocol.IPv6;

    private static string[] Candidates(ReassemblyProtocol family = Both, params ITrafficType[][] requests)
    {
        using var device = new NetworkDevice(NullLogger<NetworkDevice>.Instance, new FakeNetworkInterface("test"),
            DispatchProxy.Create<ILiveDevice, ReconfigurationLifecycleTests.NativeDevice>());
        using var filter = new BerkeleyPacketFilter
        {
            Device = device,
            Options = default,
            ReassemblyOptions = new() { Protocol = family },
            Logger = NullLogger<BerkeleyPacketFilter>.Instance,
            Monitor = null
        };
        foreach (var types in requests) _ = new TrafficFilterRequest(types, filter);
        var shape = typeof(BerkeleyPacketFilter).GetMethod("Aggregate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(filter, null);
        return ((IEnumerable<string>)typeof(BerkeleyPacketFilter).GetMethod("BuildFilterCandidates", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [shape])!).ToArray();
    }

    private static ITrafficType Family(bool ipv6) => ipv6 ? new IPv6TrafficType() : new IPv4TrafficType();
    private static BpfProgram Compile(string expression) => BpfProgram.Create(LinkLayers.Ethernet, expression, 1, uint.MaxValue);

    // These tests execute compiled BPF, without opening a capture device or requiring privileges.
    // Keep the rest of the test suite usable on machines without libpcap/Npcap installed.
    public sealed class PcapTheoryAttribute : TheoryAttribute
    {
        public PcapTheoryAttribute()
        {
            try { using var filter = Compile("ip"); }
            catch (DllNotFoundException) { Skip = "Requires libpcap or Npcap."; }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void NoRequestsProduceAnExplicitEmptyCapture(int flags)
    {
        Assert.All(Candidates((ReassemblyProtocol)flags), filter => Assert.Equal("len = 0", filter));
    }

    [PcapTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ReassemblyAndPassthroughFlagsGateFragmentsAtEveryRung(int flags)
    {
        foreach (var expression in Candidates((ReassemblyProtocol)flags, [new TCPTrafficType(80, WithData: true)]))
        {
            using var filter = Compile(expression);
            foreach (bool ipv6 in new[] { false, true })
            {
                bool enabled = (flags & (ipv6 ? 2 : 1)) != 0;
                foreach (int tags in Enumerable.Range(0, 5))
                {
                    Assert.Equal(enabled, filter.Matches(Tag(Fragment(ipv6), tags)));
                    Assert.Equal(enabled, filter.Matches(Tag(Fragment(ipv6, offset: 24), tags)));
                }
                Assert.True(filter.Matches(Ordinary(ipv6, 6)));
            }
        }
    }

    [PcapTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FamilyAndProtocolRequirementsSurviveSimplification(bool ipv6)
    {
        foreach (var expression in Candidates(Both, [Family(ipv6), new TCPTrafficType(80, WithData: true)]))
        {
            using var filter = Compile(expression);
            foreach (int tags in Enumerable.Range(0, 5))
            {
                Assert.True(filter.Matches(Tag(Fragment(ipv6), tags)));
                Assert.False(filter.Matches(Tag(Fragment(!ipv6), tags)));
                Assert.False(filter.Matches(Tag(Fragment(!ipv6, offset: 24), tags)));
                Assert.False(filter.Matches(Tag(Fragment(ipv6, protocol: 17), tags)));
                Assert.False(filter.Matches(Tag(Fragment(ipv6, protocol: 132), tags)));
                Assert.False(filter.Matches(Tag(Ordinary(ipv6, 17), tags)));
            }
            Assert.False(filter.Matches(Ordinary(!ipv6, 6)));
        }
    }

    [PcapTheory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void AddressResolutionAndWolDoNotIntroduceFragmentDemand(int flags)
    {
        foreach (var expression in Candidates((ReassemblyProtocol)flags, [new ARPTrafficType(), new NDPTrafficType(), new WOLTrafficType()]))
        {
            using var filter = Compile(expression);
            foreach (bool ipv6 in new[] { false, true })
            {
                Assert.False(filter.Matches(Fragment(ipv6)));
                Assert.False(filter.Matches(Fragment(ipv6, offset: 24)));
                Assert.False(filter.Matches(Fragment(ipv6, protocol: ipv6 ? (byte)58 : (byte)1, type: 135)));
            }
            Assert.True(filter.Matches(Ordinary(true, 58, type: 135)));
        }
        using var empty = Compile(Candidates((ReassemblyProtocol)flags)[0]);
        Assert.False(empty.Matches(Fragment(false)));
        Assert.False(empty.Matches(Ordinary(true, 6)));
    }

    [PcapTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FragmentAllowanceDefersPortsAndFlagsWhileOrdinaryTrafficRemainsPrecise(bool ipv6, bool payload)
    {
        using var filter = Compile(Candidates(Both, [Family(ipv6), new TCPTrafficType(80, WithData: payload)])[0]);
        foreach (int tags in Enumerable.Range(0, 5))
        {
            Assert.True(filter.Matches(Tag(Fragment(ipv6), tags)));
            Assert.True(filter.Matches(Tag(Fragment(ipv6, source: 80, target: 50000), tags)));
            Assert.True(filter.Matches(Tag(Fragment(ipv6, target: 81), tags)));
            Assert.True(filter.Matches(Tag(Fragment(ipv6, syn: false), tags)));
            Assert.True(filter.Matches(Tag(Fragment(ipv6, target: 81, syn: false, offset: 24), tags)));
            Assert.Equal(payload, filter.Matches(Ordinary(ipv6, 6, syn: false)));
            Assert.False(filter.Matches(Ordinary(ipv6, 6, target: 81)));
        }
    }

    [PcapTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void SplitIpv4TcpHeadersDoNotNeedTransportParsingInBpf(int tags)
    {
        using var filter = Compile(Candidates(Both, [new IPv4TrafficType(), new TCPTrafficType(80)])[0]);
        // Only eight transport bytes. Neither TCP flags nor Ethernet padding affect admission.
        // IPv4 options do not affect the IP protocol and fragment offset checks.
        var first = WithIpv4Options(Fragment(false, length: 8, syn: false));
        Array.Resize(ref first, 80);
        Assert.True(filter.Matches(Tag(first, tags)));
        Assert.True(filter.Matches(Tag(WithIpv4Options(Fragment(false, length: 8, target: 81)), tags)));
        Assert.True(filter.Matches(Tag(WithIpv4Options(Fragment(false, syn: false)), tags)));
        Assert.True(filter.Matches(Tag(Fragment(false, offset: 8, length: 16), tags)));
        Assert.False(filter.Matches(Tag(Fragment(false, protocol: 17, offset: 8, length: 16), tags)));
    }

    [PcapTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UdpAndIcmpFragmentsUseProtocolsWithoutRepeatingPortsOrTypes(bool ipv6)
    {
        using var udp = Compile(Candidates(Both, [Family(ipv6), new UDPTrafficType(80)])[0]);
        using var icmp = Compile(Candidates(Both, [Family(ipv6), new ICMPEchoTrafficType()])[0]);
        Assert.True(udp.Matches(Fragment(ipv6, protocol: 17)));
        Assert.True(udp.Matches(Fragment(ipv6, protocol: 17, target: 81)));
        Assert.False(udp.Matches(Ordinary(ipv6, 17, target: 81)));
        Assert.True(udp.Matches(Fragment(ipv6, protocol: 17, target: 81, offset: 24)));
        byte protocol = ipv6 ? (byte)58 : (byte)1;
        foreach (int type in ipv6 ? new[] { 128, 129 } : new[] { 0, 8 })
            Assert.True(icmp.Matches(Fragment(ipv6, protocol: protocol, type: type)));
        Assert.True(icmp.Matches(Fragment(ipv6, protocol: protocol, type: 3)));
        Assert.False(icmp.Matches(Ordinary(ipv6, protocol, type: 3)));
        Assert.True(icmp.Matches(Fragment(ipv6, protocol: protocol, type: 3, offset: 24)));
        Assert.False(icmp.Matches(Fragment(ipv6, protocol: 6)));
    }

    [PcapTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynAndPayloadRequirementsRemainAlternatives(bool ipv6)
    {
        using var filter = Compile(Candidates(Both,
            [Family(ipv6), new TCPTrafficType(80), new TCPTrafficType(443, WithData: true)])[0]);
        Assert.True(filter.Matches(Ordinary(ipv6, 6, target: 80)));
        Assert.False(filter.Matches(Ordinary(ipv6, 6, target: 80, syn: false)));
        Assert.True(filter.Matches(Ordinary(ipv6, 6, target: 443, syn: false)));
        Assert.False(filter.Matches(Ordinary(ipv6, 6, target: 444)));
    }

    [PcapTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconditionalBaselineStillMeansSynUdpAndEcho(bool ipv6)
    {
        var shapeType = typeof(BerkeleyPacketFilter).GetNestedType("IPTrafficShape", BindingFlags.NonPublic)!;
        var shape = Activator.CreateInstance(shapeType)!;
        shapeType.GetMethod("MakeUnconditional")!.Invoke(shape, null);
        var family = ipv6 ? System.Net.Sockets.AddressFamily.InterNetworkV6 : System.Net.Sockets.AddressFamily.InterNetwork;
        string expression = (string)shapeType.GetMethod("ToBPFExpression", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(shape, [family, true])!;
        using var filter = Compile(expression);
        Assert.True(filter.Matches(Ordinary(ipv6, 6)));
        Assert.False(filter.Matches(Ordinary(ipv6, 6, syn: false)));
        Assert.True(filter.Matches(Ordinary(ipv6, 17, target: 12345)));
        Assert.True(filter.Matches(Ordinary(ipv6, ipv6 ? (byte)58 : (byte)1, type: ipv6 ? 128 : 8)));
        Assert.False(filter.Matches(Ordinary(ipv6, 132)));
    }

    [PcapTheory]
    [InlineData(0)]
    [InlineData(2)]
    public void FamiliesKeepTheirOwnProtocolsAcrossEveryRung(int tags)
    {
        foreach (string expression in Candidates(Both,
            [new IPv4TrafficType(), new TCPTrafficType(80)],
            [new IPv6TrafficType(), new UDPTrafficType(53)]))
        {
            using var filter = Compile(expression);
            Assert.True(filter.Matches(Tag(Fragment(false), tags)));
            Assert.True(filter.Matches(Tag(Fragment(true, protocol: 17, target: 53), tags)));
            Assert.False(filter.Matches(Tag(Fragment(true), tags)));
            Assert.False(filter.Matches(Tag(Fragment(false, protocol: 17, target: 53), tags)));
        }
    }

    [PcapTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Ipv6UndecidableFragmentsAreAcceptedOnlyForRequestedFamilies(int tags)
    {
        using var filter = Compile(Candidates(Both, [new IPv6TrafficType(), new TCPTrafficType(80)])[0]);
        using var otherFamily = Compile(Candidates(Both, [new IPv4TrafficType(), new TCPTrafficType(80)])[0]);
        var data = IPReassemblyTests.Datagram(tcp: true);
        data[13] = 2;
        byte[] withDestinationOptions = new byte[data.Length + 8];
        withDestinationOptions[0] = 6;
        data.CopyTo(withDestinationOptions, 8);
        var packets = new[]
        {
            Fragment(true, extensions: true),
            Fragment(true, extensions: true, offset: 24),
            IPReassemblyTests.Fragment(true, withDestinationOptions, 0, 32, true, protocol: 60).CapturedBytes,
            IPReassemblyTests.Fragment(true, withDestinationOptions, 32, 2, false, protocol: 60).CapturedBytes,
            // RFC 8200: a later fragment's Next Header need not agree with the first one's.
            Fragment(true, protocol: 17, offset: 24),
        };
        foreach (var packet in packets)
        {
            Assert.True(filter.Matches(Tag(packet, tags)));
            Assert.False(otherFamily.Matches(Tag(packet, tags)));
        }
        // Without protochain, extension chains must be conservatively admitted, even when
        // unfragmented. The exception is still limited to the requested family.
        var ordinary = Ordinary(true, 6);
        byte[] unfragmented = new byte[ordinary.Length + 8];
        ordinary.AsSpan(0, 54).CopyTo(unfragmented);
        ordinary.AsSpan(54).CopyTo(unfragmented.AsSpan(62));
        unfragmented[20] = 0; // Hop-by-hop, followed directly by TCP, without a Fragment header
        unfragmented[54] = 6;
        Put16(unfragmented, 18, unfragmented.Length - 54);
        Assert.True(filter.Matches(Tag(unfragmented, tags)));
        Assert.False(otherFamily.Matches(Tag(unfragmented, tags)));
    }

    [PcapTheory]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(100)]
    public void FragmentOverheadIsBoundedAndIndependentOfServiceCount(int count)
    {
        ITrafficType[] Types(int ports) => [new TCPTrafficType(null, WithData: true),
            .. Enumerable.Range(14000, ports).Select(port => (ITrafficType)new UDPTrafficType((ushort)port))];
        int Overhead(int ports) => Candidates(Both, Types(ports))[0].Length - Candidates(ReassemblyProtocol.None, Types(ports))[0].Length;
        Assert.Equal(Overhead(1), Overhead(count));
        string expression = Candidates(Both, Types(count))[0];
        Assert.DoesNotContain("protochain", expression);
        using var program = Compile(expression);
        // bpf_program starts with bf_len, the number of compiled instructions. Leave ample
        // room below the common 4096-instruction limit even with a hundred services.
        Assert.InRange(Marshal.ReadInt32(program.DangerousGetHandle()), 1, 1024);
    }

    [PcapTheory]
    [InlineData(0)]
    public void ReportedConfigurationCompilesToASmallOptimizedProgram(int _)
    {
        ushort[] ports = [9, 67, 5353, 14008, 14009, 14010, 14012, 17247, 17248, 17249, 17251,
            30086, 30087, 30088, 30090, 30441, 30442, 30443, 30445];
        ITrafficType[] types = [new ARPTrafficType(), new NDPTrafficType(), new WOLTrafficType(),
            new TCPTrafficType(null, WithData: true), .. ports.Select(port => (ITrafficType)new UDPTrafficType(port))];
        foreach (string expression in Candidates(Both, types))
        {
            Assert.True(expression.Length < 3000, $"Expression has {expression.Length} characters.");
            Assert.DoesNotContain("protochain", expression);
            using var program = Compile(expression);
            Assert.InRange(Marshal.ReadInt32(program.DangerousGetHandle()), 1, 512);
        }
    }

    [PcapTheory]
    [InlineData(LinkLayers.Ieee80211)]
    [InlineData(LinkLayers.Ieee80211RadioTap)]
    public void FragmentPredicateCompilesWithVariableLinkHeaderOffsets(LinkLayers linkType)
    {
        // Reproduce the exact libpcap restriction independently of whether the test host
        // supplies live Linux VLAN-offload metadata or uses Npcap's offline Ethernet compiler.
        var error = Assert.Throws<SharpPcap.PcapException>(() => BpfProgram.Create(linkType, "ip6 protochain 44", 1, uint.MaxValue));
        Assert.Contains("'protochain' not supported with variable length headers", error.Message);
        var shapeType = typeof(BerkeleyPacketFilter).GetNestedType("IPTrafficShape", BindingFlags.NonPublic)!;
        var shape = Activator.CreateInstance(shapeType)!;
        shapeType.GetMethod("MakeUnconditional")!.Invoke(shape, null);
        string expression = (string)shapeType.GetMethod("ToFragmentBPFExpression", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(shape, [true])!;
        using var program = BpfProgram.Create(linkType, expression, 1, uint.MaxValue);
        Assert.False(program.IsInvalid);
    }

    [PcapTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedFragmentsStillReassembleOutOfOrder(bool ipv6)
    {
        using var filter = Compile(Candidates(Both, [Family(ipv6), new TCPTrafficType(80)])[0]);
        var options = new ReassemblyOptions { Protocol = Both, Timeout = TimeSpan.FromSeconds(30) };
        using IPReassembly reassembly = ipv6 ? new IPv6Reassembly(options) : new IPv4Reassembly(options);
        int split = ipv6 ? 24 : 8;
        var first = Fragment(ipv6, length: split);
        var last = Fragment(ipv6, offset: split, length: IPReassemblyTests.Datagram(tcp: true).Length - split);
        if (ipv6) last[54] = 17; // exercise the first fragment's authority over Next Header
        Assert.True(filter.Matches(last));
        Assert.True(reassembly.TryReassemble((EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, last), out var capture));
        Assert.Null(capture);
        Assert.True(filter.Matches(first));
        Assert.True(reassembly.TryReassemble((EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, first), out capture));
        Assert.Equal(80, capture!.FindPacket<TcpPacket>()!.DestinationPort);
    }

    private static byte[] Fragment(bool ipv6, byte protocol = 6, ushort source = 50000, ushort target = 80,
        bool syn = true, int offset = 0, int? length = null, bool extensions = false, int? type = null)
    {
        var data = IPReassemblyTests.Datagram(tcp: true);
        Put16(data, 0, source); Put16(data, 2, target);
        data[13] = syn ? (byte)2 : (byte)16;
        if (type is int icmpType) data[0] = (byte)icmpType;
        int count = length ?? (offset == 0 ? 24 : data.Length - offset);
        return IPReassemblyTests.Fragment(ipv6, data, offset, count, offset + count < data.Length,
            protocol: protocol, extensions: extensions).CapturedBytes;
    }

    private static byte[] Ordinary(bool ipv6, byte protocol, bool syn = true, int? type = null, ushort target = 80)
    {
        var fragment = Fragment(ipv6, protocol, syn: syn, type: type, target: target);
        if (!ipv6) { Put16(fragment, 20, 0); return fragment; }
        byte[] packet = new byte[fragment.Length - 8];
        fragment.AsSpan(0, 54).CopyTo(packet);
        fragment.AsSpan(62).CopyTo(packet.AsSpan(54));
        packet[20] = protocol;
        Put16(packet, 18, packet.Length - 54);
        return packet;
    }

    private static byte[] WithIpv4Options(byte[] original)
    {
        byte[] packet = new byte[original.Length + 4];
        original.AsSpan(0, 34).CopyTo(packet);
        original.AsSpan(34).CopyTo(packet.AsSpan(38));
        packet[14] = 0x46;
        Put16(packet, 16, packet.Length - 14);
        return packet;
    }

    private static byte[] Tag(byte[] packet, int count)
    {
        for (int tag = 0; tag < count; tag++)
        {
            byte[] tagged = new byte[packet.Length + 4];
            packet.AsSpan(0, 12).CopyTo(tagged);
            Put16(tagged, 12, tag % 2 == 0 ? 0x8100 : 0x88a8);
            Put16(tagged, 14, tag + 1);
            packet.AsSpan(12).CopyTo(tagged.AsSpan(16));
            packet = tagged;
        }
        return packet;
    }

    private static void Put16(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), checked((ushort)value));
}
