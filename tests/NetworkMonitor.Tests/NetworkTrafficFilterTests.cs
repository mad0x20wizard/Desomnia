using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging.Abstractions;
using NetTools;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;
using SharpPcap;
using System.Reflection;
using MadWizard.Desomnia.Network.Device.Filter;

namespace MadWizard.Desomnia.Network.Tests
{
    public class NetworkTrafficFilterTests
    {
        private static readonly IPAddress PeerIP = IPAddress.Parse("192.0.2.10");
        private static readonly IPAddress WatchedIP = IPAddress.Parse("192.0.2.20");
        private static readonly PhysicalAddress PeerMAC = PhysicalAddress.Parse("001122334455");
        private static readonly PhysicalAddress WatchedMAC = PhysicalAddress.Parse("AABBCCDDEEFF");
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private sealed class TestWatch : HostDemandWatch
        {
            public override bool IsOnline => true;

            public void Report(DemandEvent @event) => ReportNetworkTraffic(@event);
            public void Forward(IEnumerable<INetworkCapture> captures) => ForwardPackets(captures);

            public void ReportNetworkTraffic(INetworkCapture capture, FlowDirection direction)
                => ReportNetworkTraffic(Context(capture, direction));
        }

        private sealed class PayloadFilterRule : FilterRule<IIPServicePayloadFilterContext>
        {
            public override bool Matches(IIPServicePayloadFilterContext context) => throw new MalformedPayloadException(context.Service);
        }

        private static IFilterContext Context(INetworkCapture capture, FlowDirection direction)
            => new DefaultFilterContextParser().CreateContext([capture], direction);

        private static TestWatch CreateWatch(params FilterRule[] rules)
            => CreateWatch(null!, rules);

        private static TestWatch CreateWatch(NetworkDevice device, params FilterRule[] rules)
        {
            var network = new NetworkSegment
            {
                Logger = NullLogger<NetworkSegment>.Instance,
                Device = device,
                LocalRange = null!,
                Ranges = null!
            };

            return new TestWatch
            {
                Host = new NetworkHost("jindujun") { Network = network },
                Logger = NullLogger<NetworkHostWatch>.Instance,
                Scope = null!,
                Device = device,
                Network = network,
                AdvertiseOptions = default,
                HandoffOptions = default,
                DemandOptions = default,
                Parser = new DefaultFilterContextParser(),
                Filter = new Lazy<IFilter>(() => new RuleFilter(rules))
            };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ForwardingPreservesEveryOriginalFragmentExceptEthernetAddresses(bool ipv6)
        {
            var sent = new ForwardedFrames();
            var proxyMac = PhysicalAddress.Parse("020304050607");
            using var device = new NetworkDevice(NullLogger<NetworkDevice>.Instance,
                new FakeNetworkInterface("test") { PhysicalAddress = proxyMac },
                DispatchProxy.Create<ILiveDevice, ReconfigurationLifecycleTests.NativeDevice>()) { Filters = [sent] };
            var watch = CreateWatch(device);
            watch.Host.PhysicalAddress = WatchedMAC;
            var data = IPReassemblyTests.Datagram();
            var first = IPReassemblyTests.Fragment(ipv6, data, 0, 16, true);
            var last = IPReassemblyTests.Fragment(ipv6, data, 16, data.Length - 16, false);
            first.DestinationHardwareAddress = last.DestinationHardwareAddress = proxyMac;
            byte[][] originals = [first.CapturedBytes.ToArray(), last.CapturedBytes.ToArray()];
            var options = new ReassemblyOptions { Protocol = ReassemblyProtocol.IPv4 | ReassemblyProtocol.IPv6, Timeout = TimeSpan.FromSeconds(30) };
            using IPReassembly reassembly = ipv6
                ? new IPv6Reassembly(options) { Logger = NullLogger<IPReassembly>.Instance }
                : new IPv4Reassembly(options) { Logger = NullLogger<IPReassembly>.Instance };
            reassembly.TryReassemble(last, out _);
            reassembly.TryReassemble(first, out var capture);
            Assert.NotNull(capture);
            Assert.NotNull(first.Extract<UdpPacket>()); // Exercise a decoded first fragment too.
            Assert.NotNull(capture.FindPacket<UdpPacket>());
            watch.Forward([capture]);
            Assert.Equal(2, sent.Frames.Count);
            for (int i = 0; i < originals.Length; i++)
            {
                Assert.Equal(originals[i].Length, sent.Frames[i].Length);
                Assert.Equal(WatchedMAC.GetAddressBytes(), sent.Frames[i][..6]);
                Assert.Equal(proxyMac.GetAddressBytes(), sent.Frames[i][6..12]);
                Assert.Equal(originals[i][12..], sent.Frames[i][12..]);
            }
            Assert.Equal(originals[0], first.CapturedBytes);
            Assert.Equal(originals[1], last.CapturedBytes);
        }

        private sealed class ForwardedFrames : IDevicePacketFilter
        {
            public List<byte[]> Frames { get; } = [];
            public bool FilterIncoming(PacketCapture capture) => false;
            public bool FilterOutgoing(Packet packet)
            {
                Frames.Add(packet.CapturedBytes.ToArray());
                return true; // Inspect the actual outgoing path without sending onto a live interface.
            }
        }

        private static NetworkServiceWatch AddService(TestWatch watch, IPProtocol protocol, params FilterRule[] rules)
        {
            var service = new NetworkServiceWatch(new NetworkService("SMB", new(protocol, 445)))
            {
                Filter = new Lazy<IFilter>(() => new RuleFilter(watch.Filter.Value.Rules.Concat(rules).ToArray()))
            };
            watch.Host.AddService(service.Service);
            watch.StartTracking(service, adopt: false);
            return service;
        }

        private static EthernetCapture Packet(FlowDirection direction, IPProtocol protocol = IPProtocol.TCP,
            ushort watchedPort = 445, IPAddress? peer = null, bool ipv6 = false)
        {
            var inbound = direction == FlowDirection.Inbound;
            var watched = ipv6 ? IPAddress.Parse("2001:db8::20") : WatchedIP;
            peer ??= ipv6 ? IPAddress.Parse("2001:db8::10") : PeerIP;
            ushort peerPort = watchedPort == 445 ? (ushort)50000 : (ushort)445;
            ushort sourcePort = inbound ? peerPort : watchedPort;
            ushort targetPort = inbound ? watchedPort : peerPort;
            TransportPacket transport = protocol == IPProtocol.TCP
                ? new TcpPacket(sourcePort, targetPort) { Acknowledgment = true }
                : new UdpPacket(sourcePort, targetPort);
            transport.PayloadData = [1, 2, 3, 4];
            IPPacket ip = ipv6
                ? new IPv6Packet(inbound ? peer : watched, inbound ? watched : peer)
                : new IPv4Packet(inbound ? peer : watched, inbound ? watched : peer);
            ip.PayloadPacket = transport;
            return new EthernetCapture(new EthernetPacket(inbound ? PeerMAC : WatchedMAC, inbound ? WatchedMAC : PeerMAC,
                ipv6 ? EthernetType.IPv6 : EthernetType.IPv4) { PayloadPacket = ip });
        }

        [Theory]
        [InlineData(FlowDirection.Inbound, IPProtocol.TCP)]
        [InlineData(FlowDirection.Outbound, IPProtocol.TCP)]
        [InlineData(FlowDirection.Inbound, IPProtocol.UDP)]
        [InlineData(FlowDirection.Outbound, IPProtocol.UDP)]
        public void ServiceTraffic_UsesItsPortAndPeerRulesInBothDirections(FlowDirection direction, IPProtocol protocol)
        {
            var watch = CreateWatch(new StaticHostFilterRule { Type = FilterRuleType.MustNot, Addresses = [WatchedIP] });

            IEnumerable<HostFilterRule> hostRules = [new StaticHostFilterRule { Type = FilterRuleType.Must, Addresses = [PeerIP] }];

            var service = AddService(watch, protocol, hostRules.ToArray());

            watch.ReportNetworkTraffic(Packet(direction, protocol, peer: IPAddress.Parse("192.0.2.30")), direction);
            Assert.Empty(watch.Inspect(Interval));
            Assert.Empty(service.Inspect(Interval));

            var packet = Packet(direction, protocol);
            byte[] original = (byte[])packet.Single().Bytes.Clone();
            watch.ReportNetworkTraffic(packet, direction);
            Assert.Equal(original, packet.Single().Bytes);
            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(Interval)));
            Assert.Equal(4, usage.Bytes);
            Assert.Equal(4, Assert.IsType<NetworkServiceUsage>(Assert.Single(usage.Tokens)).Bytes);
        }

        [Theory]
        [InlineData(FlowDirection.Inbound, false)]
        [InlineData(FlowDirection.Outbound, false)]
        [InlineData(FlowDirection.Inbound, true)]
        [InlineData(FlowDirection.Outbound, true)]
        public void SMBClientKeepalive_DoesNotCountAsWatchedHostOrServiceUsage(FlowDirection direction, bool ipv6)
        {
            var watch = CreateWatch();
            var service = AddService(watch, IPProtocol.TCP);

            // jindujun:50000 <-> DRAGON:445 never involves jindujun's service port.
            var packet = Packet(direction, watchedPort: 50000, ipv6: ipv6);
            watch.ReportNetworkTraffic(packet, direction);
            packet = new EthernetCapture(packet.Single().MakeCopy());
            packet.FindPacket<TcpPacket>()!.PayloadData = []; // pure ACK must not count as activity either
            watch.ReportNetworkTraffic(packet, direction);

            Assert.Empty(watch.Inspect(Interval));
            Assert.Empty(service.Inspect(Interval));
        }

        [Theory]
        [InlineData(FlowDirection.Inbound)]
        [InlineData(FlowDirection.Outbound)]
        public void ServiceSelectionRejectsUnwatchedPortsAndInheritsHostVetoes(FlowDirection direction)
        {
            var blocked = IPAddress.Parse("192.0.2.30");
            var watch = CreateWatch(
                new StaticHostRangeFilterRule { Type = FilterRuleType.Must, Range = IPAddressRange.Parse("192.0.2.0/24") },
                new StaticHostFilterRule { Type = FilterRuleType.MustNot, Addresses = [blocked] });
            var service = AddService(watch, IPProtocol.TCP);

            watch.ReportNetworkTraffic(Packet(direction, watchedPort: 50000, peer: blocked), direction);
            Assert.Empty(watch.Inspect(Interval));
            watch.ReportNetworkTraffic(Packet(direction, peer: blocked), direction);
            Assert.Empty(watch.Inspect(Interval)); // the service's composite retains the host veto
            Assert.Empty(service.Inspect(Interval));

            watch.ReportNetworkTraffic(Packet(direction, watchedPort: 50000), direction);
            Assert.Empty(watch.Inspect(Interval));

            var packet = Packet(direction);
            packet.FindPacket<TcpPacket>()!.PayloadData = [];
            watch.ReportNetworkTraffic(packet, direction);
            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(Interval)));
            Assert.Equal(0, usage.Bytes); // accepted ACK is activity even without payload
            Assert.Equal(0, Assert.IsType<NetworkServiceUsage>(Assert.Single(usage.Tokens)).Bytes);
        }

        [Theory]
        [InlineData(FlowDirection.Inbound)]
        [InlineData(FlowDirection.Outbound)]
        public void MissingPayload_DoesNotCountOrStartARequest(FlowDirection direction)
        {
            var watch = CreateWatch();
            var service = AddService(watch, IPProtocol.TCP, new PayloadFilterRule { Type = FilterRuleType.Must });
            var packet = Packet(direction);

            Assert.Throws<MalformedPayloadException>(() => watch.Verify(Context(packet, direction)));
            if (direction == FlowDirection.Inbound)
                Assert.Null(watch.Evaluate(packet));
            else
                watch.ReportNetworkTraffic(packet, direction);

            Assert.Empty(watch.Inspect(Interval));
            Assert.Empty(service.Inspect(Interval));
        }

        [Fact]
        public void DemandEventAccounting_FiltersEachPacket()
        {
            var watch = CreateWatch();
            AddService(watch, IPProtocol.TCP);
            watch.Report(new DemandEvent(watch.Host, packets:
                [Packet(FlowDirection.Inbound), Packet(FlowDirection.Inbound, watchedPort: 50000)]));

            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(Interval)));
            Assert.Equal(4, usage.Bytes);
            Assert.Equal(4, Assert.IsType<NetworkServiceUsage>(Assert.Single(usage.Tokens)).Bytes);
        }

        [Fact]
        public void ServiceSelectionOnlyRequestsIPFromInboundAddressResolution()
        {
            var watch = CreateWatch();
            AddService(watch, IPProtocol.TCP);
            Assert.Throws<IPTrafficNeededException>(() => watch.Verify(
                new CaptureFilterContext(PeerIP) { Direction = FlowDirection.Inbound }));
            Assert.False(watch.Verify(new CaptureFilterContext(PeerIP) { Direction = FlowDirection.Outbound }));
            Assert.False(watch.Verify(new ICMPCaptureFilterContext(PeerIP, ICMPType.Echo) { Direction = FlowDirection.Inbound }));
            Assert.False(watch.Verify(new IPCaptureFilterContext(PeerIP) { Direction = FlowDirection.Inbound }));
        }

        [Theory]
        [InlineData(FlowDirection.Inbound, false)]
        [InlineData(FlowDirection.Outbound, false)]
        [InlineData(FlowDirection.Inbound, true)]
        [InlineData(FlowDirection.Outbound, true)]
        public void RouterPolicy_UsesThePeerAddress(FlowDirection direction, bool allowWake)
        {
            var watch = CreateWatch();
            var router = new NetworkRouter("router")
            {
                Network = watch.Network,
                Options = new RouterOptions { AllowWake = allowWake, AllowWakeByVPNClients = true },
                VPNClients = []
            };
            router.AddAddress(PeerIP);
            watch.Network.AddHost(router);
            IFilter filter = new RouterFilter
            {
                Logger = NullLogger<RouterFilter>.Instance,
                Network = watch.Network,
                Reachability = null! // unicast accounting must not probe VPN clients
            };

            Assert.Equal(!allowWake, filter.ShouldFilter(Context(Packet(direction), direction)));
            Assert.False(filter.ShouldFilter(Context(Packet(direction, peer: IPAddress.Parse("192.0.2.30")), direction)));
        }
    }
}
