using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class EthernetCaptureTests
    {
        private static readonly PhysicalAddress SourceMAC = PhysicalAddress.Parse("001122334455");
        private static readonly PhysicalAddress TargetMAC = PhysicalAddress.Parse("AABBCCDDEEFF");
        private static readonly PhysicalAddress BroadcastMAC = PhysicalAddress.Parse("FFFFFFFFFFFF");
        private static readonly IPAddress SourceIP = IPAddress.Parse("192.0.2.10");
        private static readonly IPAddress TargetIP = IPAddress.Parse("192.0.2.20");

        private sealed class ObservedPacket : Packet
        {
            public int PayloadReads { get; set; }

            public ObservedPacket()
            {
                Header = new([]);
            }

            public override Packet PayloadPacket
            {
                get { PayloadReads++; return base.PayloadPacket; }
                set => base.PayloadPacket = value;
            }
        }

        private static EthernetPacket TransportPacket(bool ipv6, IPProtocol protocol, byte[] payload)
        {
            TransportPacket transport = protocol == IPProtocol.TCP
                ? new TcpPacket(50000, 445) { Synchronize = true, PayloadData = payload }
                : new UdpPacket(50000, 445) { PayloadData = payload };
            IPPacket ip = ipv6
                ? new IPv6Packet(IPAddress.Parse("2001:db8::10"), IPAddress.Parse("2001:db8::20"))
                : new IPv4Packet(SourceIP, TargetIP);
            ip.PayloadPacket = transport;
            return new EthernetPacket(SourceMAC, TargetMAC, ipv6 ? EthernetType.IPv6 : EthernetType.IPv4)
            {
                PayloadPacket = ip
            };
        }

        private static void AssertOriginalFrameMetadata(EthernetCapture capture, EthernetPacket packet)
        {
            Assert.Equal(packet.SourceHardwareAddress, capture.SourcePhysicalAddress);
            Assert.Equal(packet.Type, capture.Type);
            Assert.Same(packet, Assert.Single(capture));
        }

        [Fact]
        public void ConstructionDoesNotWalkLayersAndMetadataReferencesAreReused()
        {
            var ip = new IPv4Packet(SourceIP, TargetIP) { PayloadPacket = new TcpPacket(50000, 445) };
            var observed = new ObservedPacket { PayloadPacket = ip };
            var packet = new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv4) { PayloadPacket = observed };
            observed.PayloadReads = 0;

            var capture = new EthernetCapture(packet);
            Assert.Equal(0, observed.PayloadReads);
            Assert.Equal(SourceMAC, capture.SourcePhysicalAddress);
            Assert.Equal(0, observed.PayloadReads);

            var source = Assert.IsType<IPServiceEndPoint>(capture.SourceEndPoint);
            Assert.Equal(50000, source.Port);
            Assert.Equal(SourceIP, source.Address);
            Assert.Same(source, capture.SourceEndPoint);
            Assert.Same(source.Address, capture.SourceEndPoint!.Address);

            Assert.Equal(TargetIP, capture.TargetEndPoint!.Address);
            Assert.Same(capture.TargetEndPoint, capture.TargetEndPoint);
            Assert.Same(capture.SourcePhysicalAddress, capture.SourcePhysicalAddress);
            Assert.Same(capture.TargetPhysicalAddress, capture.TargetPhysicalAddress);
        }

        [Fact]
        public void MissingMetadataIsRememberedButPacketReferencesAreNotCached()
        {
            var observed = new ObservedPacket { PayloadPacket = new WakeOnLanPacket(TargetMAC) };
            var packet = new EthernetPacket(SourceMAC, TargetMAC, (EthernetType)0) { PayloadPacket = observed };
            INetworkCapture capture = new EthernetCapture(packet);

            Assert.False(capture.HasPacket<TransportPacket>(out var transport));
            Assert.Null(transport);
            int reads = observed.PayloadReads;
            Assert.True(reads > 0);
            Assert.False(capture.HasPacket<TransportPacket>());
            Assert.True(observed.PayloadReads > reads);

            Assert.Null(capture.SourceEndPoint);
            Assert.Null(capture.TargetEndPoint);
            Assert.False(capture.HasPacket<TcpPacket>());
            Assert.Equal(0, capture.PayloadLength);
            Assert.Empty(capture.PayloadData);
            reads = observed.PayloadReads;

            for (int i = 0; i < 5; i++)
            {
                Assert.Null(capture.SourceEndPoint);
                Assert.Null(capture.TargetEndPoint);
                Assert.Equal(0, capture.PayloadLength);
                Assert.Empty(capture.PayloadData);
            }
            Assert.Equal(reads, observed.PayloadReads);
        }

        [Theory]
        [InlineData(false, IPProtocol.TCP)]
        [InlineData(false, IPProtocol.UDP)]
        [InlineData(true, IPProtocol.TCP)]
        [InlineData(true, IPProtocol.UDP)]
        public void TransportMetadataReusesAddressesEndpointsAndPayload(bool ipv6, IPProtocol protocol)
        {
            byte[] payload = [1, 2, 3, 4, 5];
            var packet = TransportPacket(ipv6, protocol, payload).MakeCopy();
            var originalBytes = (byte[])packet.Bytes.Clone();
            var capture = new EthernetCapture(packet);

            AssertOriginalFrameMetadata(capture, packet);
            Assert.True(capture.IsIPUnicast);
            var source = Assert.IsType<IPServiceEndPoint>(capture.SourceEndPoint);
            var target = Assert.IsType<IPServiceEndPoint>(capture.TargetEndPoint);
            Assert.Equal(protocol, source.Protocol);
            Assert.Equal(protocol, target.Protocol);
            Assert.Equal(50000, source.Port);
            Assert.Equal(445, target.Port);
            Assert.Same(source.Address, capture.SourceEndPoint!.Address);
            Assert.Same(target.Address, capture.TargetEndPoint!.Address);
            Assert.Same(source, capture.SourceEndPoint);
            Assert.Same(target, capture.TargetEndPoint);
            Assert.Equal(payload.Length, capture.PayloadLength);
            Assert.Equal(payload, capture.PayloadData);
            Assert.Same(capture.PayloadData, capture.PayloadData);
            Assert.Equal(originalBytes, packet.Bytes);
        }

        [Fact]
        public void SynMetadataDescribesPayloadAcquisitionWithoutPayload()
        {
            var capture = new EthernetCapture(TransportPacket(false, IPProtocol.TCP, []));
            var target = Assert.IsType<IPServiceEndPoint>(capture.TargetEndPoint);
            var needed = new ServicePayloadNeededException(target, capture.SourceEndPoint!);

            Assert.Equal(new IPService(IPProtocol.TCP, 445), needed.Service);
            Assert.Same(capture.SourceEndPoint, needed.RemoteEndPoint);
            Assert.Equal(SourceIP, needed.RemoteEndPoint.Address);
            Assert.Equal(50000, needed.RemoteEndPoint.Port);
            Assert.IsAssignableFrom<AdditionalDataNeededException>(needed);
            Assert.Equal(0, capture.PayloadLength);
            Assert.Empty(capture.PayloadData);
        }

        [Theory]
        [InlineData(ArpOperation.Request, "192.0.2.10", "192.0.2.20", false, false, false)]
        [InlineData(ArpOperation.Request, "192.0.2.20", "192.0.2.20", true, false, false)]
        [InlineData(ArpOperation.Response, "192.0.2.20", "192.0.2.20", false, true, false)]
        [InlineData(ArpOperation.Request, "0.0.0.0", "192.0.2.20", false, false, true)]
        public void ArpRetainsResolutionAndDuplicateDetectionSemantics(ArpOperation operation,
            string source, string target, bool announcement, bool gratuitous, bool probe)
        {
            var packet = new EthernetPacket(SourceMAC, BroadcastMAC, EthernetType.Arp)
            {
                PayloadPacket = new ArpPacket(operation, TargetMAC, IPAddress.Parse(target), SourceMAC, IPAddress.Parse(source))
            };
            var capture = new EthernetCapture(packet);

            AssertOriginalFrameMetadata(capture, packet);
            Assert.Equal(IPAddress.Parse(source), capture.SourceEndPoint!.Address);
            Assert.Equal(IPAddress.Parse(target), capture.TargetEndPoint!.Address);
            Assert.Equal(announcement, capture.IsARPAnnouncement);
            Assert.Equal(gratuitous, capture.IsARPGratuitous);
            Assert.Equal(probe, capture.IsARPProbe);
            Assert.Equal(probe, capture.IsDuplicateAddressDetection);
            Assert.False(capture.IsIPUnicast);
            Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.SourceEndPoint).Port);
            Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.TargetEndPoint).Port);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NdpSolicitationUsesResolutionTargetAndDetectsDad(bool dad)
        {
            var target = IPAddress.Parse("2001:db8::20");
            var ip = new IPv6Packet(dad ? IPAddress.IPv6Any : IPAddress.Parse("2001:db8::10"),
                IPAddress.Parse("ff02::1:ff00:20"))
                .WithNDPNeighborSolicitation(target, dad ? null : SourceMAC);
            var packet = new EthernetPacket(SourceMAC, BroadcastMAC, EthernetType.IPv6) { PayloadPacket = ip }.MakeCopy();
            var capture = new EthernetCapture(packet);

            AssertOriginalFrameMetadata(capture, packet);
            Assert.Equal(target, capture.TargetEndPoint!.Address);
            Assert.Equal(dad ? IPAddress.IPv6Any : IPAddress.Parse("2001:db8::10"), capture.SourceEndPoint!.Address);
            Assert.False(capture.IsIPUnicast);
            Assert.Equal(dad, capture.IsDuplicateAddressDetection);
            Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.SourceEndPoint).Port);
            Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.TargetEndPoint).Port);
        }

        [Fact]
        public void NdpAdvertisementRetainsIpDestinationAndIsNotIpUnicast()
        {
            var destination = IPAddress.Parse("2001:db8::10");
            var ip = new IPv6Packet(IPAddress.Parse("2001:db8::20"), destination)
                .WithNDPNeighborAdvertisement(NDPFlags.Solicited, IPAddress.Parse("2001:db8::30"), TargetMAC);
            var packet = new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv6) { PayloadPacket = ip }.MakeCopy();
            var capture = new EthernetCapture(packet);

            AssertOriginalFrameMetadata(capture, packet);
            Assert.Equal(destination, capture.TargetEndPoint!.Address);
            Assert.False(capture.IsIPUnicast);
            Assert.False(capture.IsDuplicateAddressDetection);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IcmpUsesAddressOnlyEndpoints(bool ipv6)
        {
            IPPacket ip = ipv6
                ? new IPv6Packet(IPAddress.Parse("2001:db8::10"), IPAddress.Parse("2001:db8::20"))
                : new IPv4Packet(SourceIP, TargetIP);
            ip.Protocol = ipv6 ? ProtocolType.IcmpV6 : ProtocolType.Icmp;
            ip.PayloadData = [ipv6 ? (byte)128 : (byte)8, 0, 0, 0, 0, 0, 0, 0];
            ip.PayloadLength = 8;
            var packet = new EthernetPacket(SourceMAC, TargetMAC, ipv6 ? EthernetType.IPv6 : EthernetType.IPv4)
            {
                PayloadPacket = ip
            }.MakeCopy();
            var capture = new EthernetCapture(packet);

            AssertOriginalFrameMetadata(capture, packet);
            Assert.True(capture.IsIPUnicast);
            var source = Assert.IsType<IPEndPoint>(capture.SourceEndPoint);
            var target = Assert.IsType<IPEndPoint>(capture.TargetEndPoint);
            Assert.Equal(ip.SourceAddress, source.Address);
            Assert.Equal(ip.DestinationAddress, target.Address);
            Assert.Equal(0, source.Port);
            Assert.Equal(0, target.Port);
            Assert.Equal(0, capture.PayloadLength); // ICMP data is not transport payload.
        }

        [Theory]
        [InlineData("255.255.255.255", false)]
        [InlineData("224.0.0.1", true)] // Deliberately preserve the existing IPv4 multicast interpretation.
        [InlineData("ff02::1", false)]
        public void BroadcastAndMulticastClassificationMatchesExistingBehavior(string destination, bool unicast)
        {
            var target = IPAddress.Parse(destination);
            bool ipv6 = target.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            IPPacket ip = ipv6 ? new IPv6Packet(IPAddress.IPv6Loopback, target) : new IPv4Packet(SourceIP, target);
            var packet = new EthernetPacket(SourceMAC, BroadcastMAC, ipv6 ? EthernetType.IPv6 : EthernetType.IPv4)
            {
                PayloadPacket = ip
            };
            var capture = new EthernetCapture(packet);
            AssertOriginalFrameMetadata(capture, packet);
            Assert.Equal(unicast, capture.IsIPUnicast);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WakeOnLanUsesMagicTargetAndCountsOnlyTransportPayload(bool overUdp)
        {
            var wol = new WakeOnLanPacket(TargetMAC);
            Packet payload = overUdp
                ? new IPv4Packet(SourceIP, IPAddress.Broadcast) { PayloadPacket = new UdpPacket(50000, 9) { PayloadPacket = wol } }
                : wol;
            var packet = new EthernetPacket(SourceMAC, BroadcastMAC, overUdp ? EthernetType.IPv4 : (EthernetType)0x0842)
            {
                PayloadPacket = payload
            };
            var capture = new EthernetCapture(packet);

            AssertOriginalFrameMetadata(capture, packet);
            Assert.Equal(TargetMAC, capture.TargetPhysicalAddress);
            Assert.Same(capture.TargetPhysicalAddress, capture.TargetPhysicalAddress);
            Assert.Equal(overUdp ? wol.TotalPacketLength : 0, capture.PayloadLength);
            Assert.Equal(overUdp ? wol.Bytes : [], capture.PayloadData);
            Assert.Same(capture.PayloadData, capture.PayloadData);
        }

        [Fact]
        public void SharedReferencesReuseBothExistingAndLaterCacheValues()
        {
            var capture = new EthernetCapture(TransportPacket(false, IPProtocol.TCP, []));
            var source = capture.SourceEndPoint;
            var snapshot = capture;

            Assert.Same(source, snapshot.SourceEndPoint);
            var snapshotTarget = snapshot.TargetEndPoint;
            var originalTarget = capture.TargetEndPoint;
            Assert.Equal(TargetIP, snapshotTarget!.Address);
            Assert.Equal(originalTarget, snapshotTarget);
            Assert.Same(originalTarget, snapshotTarget);
            Assert.Same(snapshotTarget, snapshot.TargetEndPoint);
            Assert.Same(originalTarget, capture.TargetEndPoint);
        }

        [Fact]
        public void RawLayerLookupDoesNotBypassTransportProtocolGuard()
        {
            var packet = TransportPacket(false, IPProtocol.TCP, [1, 2, 3]);
            packet.Extract<IPPacket>()!.Protocol = ProtocolType.Icmp; // Deliberately inconsistent tree.
            var capture = new EthernetCapture(packet);

            Assert.True(((INetworkCapture)capture).HasPacket<TransportPacket>());
            Assert.True(((INetworkCapture)capture).HasPacket<TcpPacket>());
            AssertOriginalFrameMetadata(capture, packet);
            Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.SourceEndPoint).Port);
            Assert.Equal(0, Assert.IsType<IPEndPoint>(capture.TargetEndPoint).Port);
            Assert.Equal(0, capture.PayloadLength);
            Assert.Empty(capture.PayloadData);
        }

        private static (int AddressOnly, long Allocated) MeasureCachedAddressOnlyEndPoints(ref EthernetCapture capture)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int addressOnly = 0;
            for (int i = 0; i < 1024; i++)
            {
                if (capture.SourceEndPoint is { Port: 0 } && capture.TargetEndPoint is { Port: 0 })
                    addressOnly++;
            }
            return (addressOnly, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void RepeatedAddressOnlyEndpointReadsDoNotAllocate()
        {
            var packet = new EthernetPacket(SourceMAC, BroadcastMAC, EthernetType.Arp)
            {
                PayloadPacket = new ArpPacket(ArpOperation.Request, TargetMAC, TargetIP, SourceMAC, SourceIP)
            };
            var capture = new EthernetCapture(packet);
            var source = capture.SourceEndPoint;
            var target = capture.TargetEndPoint;
            _ = MeasureCachedAddressOnlyEndPoints(ref capture);
            var result = MeasureCachedAddressOnlyEndPoints(ref capture);
            Assert.Equal(1024, result.AddressOnly);
            Assert.Equal(0, result.Allocated);
            Assert.Same(source, capture.SourceEndPoint);
            Assert.Same(target, capture.TargetEndPoint);
        }

        private static (int Bytes, long Allocated) MeasureLengthReads(EthernetPacket packet)
        {
            var capture = new EthernetCapture(packet);
            _ = capture.PayloadLength;
            long before = GC.GetAllocatedBytesForCurrentThread();
            int bytes = 0;
            for (int i = 0; i < 1024; i++)
            {
                bytes += capture.PayloadLength;
            }
            return (bytes, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CountingPayloadOnSharedCaptureDoesNotAllocate(bool decodedChild)
        {
            EthernetPacket packet;
            int length;
            if (decodedChild)
            {
                var wol = new WakeOnLanPacket(TargetMAC);
                length = wol.TotalPacketLength;
                packet = new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv4)
                {
                    PayloadPacket = new IPv4Packet(SourceIP, TargetIP)
                    {
                        PayloadPacket = new UdpPacket(50000, 9) { PayloadPacket = wol }
                    }
                };
            }
            else
            {
                length = 256;
                packet = TransportPacket(false, IPProtocol.TCP, new byte[length]).MakeCopy();
            }

            _ = MeasureLengthReads(packet); // Warm PacketDotNet's own lazy decoding and the measured code.
            var result = MeasureLengthReads(packet);
            Assert.Equal(length * 1024, result.Bytes);
            Assert.Equal(0, result.Allocated);
        }
    }
}