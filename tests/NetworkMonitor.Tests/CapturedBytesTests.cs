using MadWizard.Desomnia.Network.Device;
using PacketDotNet;
using PacketDotNet.Utils;
using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class CapturedBytesTests
{
    [Fact]
    public void CompleteDecodedCaptureReturnsItsBackingArray()
    {
        Packet packet = FilterParserPackets.Tcp(1, "payload").Single();
        _ = packet.Extract<TcpPacket>();

        Assert.Same(packet.HeaderSegment.Bytes, packet.CapturedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InnerPacketsReturnOnlyTheirCapturedBytes(bool ipv6)
    {
        var data = IPReassemblyTests.Datagram();
        var frame = IPReassemblyTests.Fragment(ipv6, data, 0, 16, true);
        byte[] before = (byte[])frame.HeaderSegment.Bytes.Clone();
        Packet ip = frame.Extract<IPPacket>();
        Packet udp = frame.Extract<UdpPacket>();

        Assert.Equal(before[14..], ip.CapturedBytes);
        Assert.Equal(data[..16], udp.CapturedBytes);
        Assert.Equal(before, frame.HeaderSegment.Bytes);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 16)]
    [InlineData(true, 16)]
    public void FragmentsKeepTheirOriginalBytesWithoutDecoding(bool ipv6, int offset)
    {
        var data = IPReassemblyTests.Datagram();
        var packet = IPReassemblyTests.Fragment(ipv6, data, offset,
            offset == 0 ? 16 : data.Length - offset, offset == 0);
        byte[] before = (byte[])packet.HeaderSegment.Bytes.Clone();

        Assert.False(packet.IsPayloadInitialized);
        Assert.Same(packet.HeaderSegment.Bytes, packet.CapturedBytes);
        Assert.Equal(before, packet.CapturedBytes.ToArray());
        Assert.False(packet.IsPayloadInitialized);
        Assert.Equal(before, packet.HeaderSegment.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodedFirstFragmentKeepsTheWholeDatagramLength(bool ipv6)
    {
        var data = IPReassemblyTests.Datagram();
        var packet = IPReassemblyTests.Fragment(ipv6, data, 0, 16, true);
        var udp = packet.Extract<UdpPacket>();
        byte[] before = (byte[])packet.HeaderSegment.Bytes.Clone();

        Assert.Equal(data.Length, udp.Length);
        Assert.Equal(before, packet.CapturedBytes.ToArray());
        Assert.Equal(data.Length, udp.Length);
        Assert.Equal(before, packet.HeaderSegment.Bytes);
    }

    [Fact]
    public void CapturedSlicePreservesPaddingAndExcludesSurroundingBuffer()
    {
        var packet = IPReassemblyTests.Fragment(false, IPReassemblyTests.Datagram(), 0, 16, true, vlan: 17);
        byte[] frame = [.. packet.CapturedBytes, 0xde, 0xad, 0xbe, 0xef];
        byte[] buffer = [1, 2, 3, .. frame, 4, 5];
        var captured = new EthernetPacket(new ByteArraySegment(buffer, 3, frame.Length, 3 + frame.Length));

        Assert.Equal(frame, captured.CapturedBytes.ToArray());
        Assert.NotSame(buffer, captured.CapturedBytes);
        Assert.False(captured.IsPayloadInitialized);
    }

    [Fact]
    public void HeaderOnlyCaptureDoesNotTryToDecodeMissingIPHeader()
    {
        byte[] frame = new byte[14];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
        var packet = (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, frame);

        Assert.Equal(frame, packet.CapturedBytes.ToArray());
        Assert.False(packet.IsPayloadInitialized);
    }

    [Fact]
    public void ConstructedPacketCopiesStoredFieldsWithoutCalculatingThem()
    {
        var udp = new UdpPacket(50000, 80) { PayloadData = [1, 2, 3], Checksum = 0xabcd };
        var ip = new IPv4Packet(FilterParserPackets.Peer, FilterParserPackets.Watched)
            { PayloadPacket = udp, TotalLength = 1234, Checksum = 0x5678 };
        var packet = new EthernetPacket(FilterParserPackets.PeerMac, FilterParserPackets.WatchedMac, EthernetType.IPv4)
            { PayloadPacket = ip };
        BinaryPrimitives.WriteUInt16BigEndian(udp.HeaderSegment.Bytes.AsSpan(udp.HeaderSegment.Offset + 4), 4096);
        byte[] ethernetBefore = packet.HeaderData, ipBefore = ip.HeaderData, udpBefore = udp.HeaderData;

        byte[] bytes = packet.CapturedBytes.ToArray();

        Assert.Equal(45, bytes.Length);
        Assert.Equal(1234, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(16)));
        Assert.Equal(4096, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(38)));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes[42..]);
        Assert.Equal(ethernetBefore, packet.HeaderData);
        Assert.Equal(ipBefore, ip.HeaderData);
        Assert.Equal(udpBefore, udp.HeaderData);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitlyPreparedOutgoingPacketHasValidLengthsAndChecksums(bool ipv6)
    {
        var udp = new UdpPacket(50000, 80) { PayloadData = [1, 2, 3] };
        IPPacket ip = ipv6
            ? new IPv6Packet(IPAddress.Parse("2001:db8::10"), IPAddress.Parse("2001:db8::20"))
            : new IPv4Packet(FilterParserPackets.Peer, FilterParserPackets.Watched);
        ip.PayloadPacket = udp;
        var packet = new EthernetPacket(FilterParserPackets.PeerMac, FilterParserPackets.WatchedMac,
            ipv6 ? EthernetType.IPv6 : EthernetType.IPv4) { PayloadPacket = ip };
        typeof(NetworkDevice).GetMethod("PreparePacketToSend", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [packet]);
        byte[] ipBefore = ip.HeaderData, udpBefore = udp.HeaderData;

        var copy = packet.MakeCapturedCopy();

        Assert.Equal(11, copy.Extract<UdpPacket>().Length);
        Assert.True(copy.Extract<UdpPacket>().ValidUdpChecksum);
        if (copy.Extract<IPv4Packet>() is { } ipv4)
        {
            Assert.Equal(31, ipv4.TotalLength);
            Assert.True(ipv4.ValidIPChecksum);
        }
        else
            Assert.Equal(11, copy.Extract<IPv6Packet>().PayloadLength);
        Assert.Equal(ipBefore, ip.HeaderData);
        Assert.Equal(udpBefore, udp.HeaderData);
    }

    [Theory]
    [InlineData(EthernetType.Arp)]
    [InlineData(EthernetType.IPv6)]
    [InlineData(EthernetType.WakeOnLan)]
    public void OutgoingDiscoveryFramesNeedNoImplicitSerialization(EthernetType type)
    {
        Packet payload = type switch
        {
            EthernetType.Arp => new ArpPacket(ArpOperation.Request, FilterParserPackets.WatchedMac,
                FilterParserPackets.Watched, FilterParserPackets.PeerMac, FilterParserPackets.Peer),
            EthernetType.IPv6 => new IPv6Packet(IPAddress.Parse("fe80::10"), IPAddress.Parse("ff02::2"))
                .WithNDPRouterSolicitation(),
            _ => new WakeOnLanPacket(FilterParserPackets.WatchedMac)
        };
        var packet = new EthernetPacket(FilterParserPackets.PeerMac, FilterParserPackets.WatchedMac, type)
            { PayloadPacket = payload };

        byte[] captured = packet.CapturedBytes.ToArray();

        // These existing sending paths already construct complete headers without prepare=true.
        Assert.Equal(packet.Bytes, captured);
    }
}
