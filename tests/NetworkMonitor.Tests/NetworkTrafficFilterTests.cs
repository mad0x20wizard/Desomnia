using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging.Abstractions;
using NetTools;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

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
        }

        private sealed class PayloadFilterRule() : TCPServiceFilterRule(445)
        {
            protected override bool MatchesPayload(byte[] payload) => throw new ServicePayloadNeededException(Port);
        }

        private static TestWatch CreateWatch(params PacketFilterRule[] rules)
        {
            var network = new NetworkSegment
            {
                Logger = NullLogger<NetworkSegment>.Instance,
                Device = null!,
                LocalRange = null!,
                Ranges = null!
            };

            return new TestWatch
            {
                Host = new NetworkHost("jindujun") { Network = network },
                Logger = NullLogger<NetworkHostWatch>.Instance,
                Scope = null!,
                Device = null!,
                Network = network,
                AdvertiseOptions = default,
                HandoffOptions = default,
                DemandOptions = default,
                DefaultFilterOptions = default,
                Filter = new Lazy<IPacketFilter>(() => new PacketRuleFilter(rules))
            };
        }

        private static ServiceFilterWatch AddService(TestWatch watch, IPProtocol protocol, params PacketFilterRule[] rules)
        {
            var service = new ServiceFilterWatch(new TransportNetworkService("SMB", new(protocol, 445)))
            {
                Filter = new Lazy<IPacketFilter>(() => new CompositePacketFilter([watch.Filter.Value, new PacketRuleFilter(rules)]))
            };
            watch.StartTracking(service, adopt: false);
            return service;
        }

        private static EthernetPacket Packet(PacketDirection direction, IPProtocol protocol = IPProtocol.TCP,
            ushort watchedPort = 445, IPAddress? peer = null, bool ipv6 = false)
        {
            var inbound = direction == PacketDirection.Inbound;
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
            return new EthernetPacket(inbound ? PeerMAC : WatchedMAC, inbound ? WatchedMAC : PeerMAC,
                ipv6 ? EthernetType.IPv6 : EthernetType.IPv4) { PayloadPacket = ip };
        }

        [Theory]
        [InlineData(PacketDirection.Inbound, IPProtocol.TCP)]
        [InlineData(PacketDirection.Outbound, IPProtocol.TCP)]
        [InlineData(PacketDirection.Inbound, IPProtocol.UDP)]
        [InlineData(PacketDirection.Outbound, IPProtocol.UDP)]
        public void ServiceTraffic_UsesItsPortAndPeerRulesInBothDirections(PacketDirection direction, IPProtocol protocol)
        {
            var watch = CreateWatch(new StaticHostFilterRule { Type = FilterRuleType.MustNot, Addresses = [WatchedIP] });
            TransportFilterRule rule = protocol == IPProtocol.TCP
                ? new TCPServiceFilterRule(445) { Type = FilterRuleType.Must }
                : new UDPServiceFilterRule(445) { Type = FilterRuleType.Must };
            rule.HostRules = [new StaticHostFilterRule { Type = FilterRuleType.Must, Addresses = [PeerIP] }];
            var service = AddService(watch, protocol, rule);

            watch.ReportNetworkTraffic(Packet(direction, protocol, peer: IPAddress.Parse("192.0.2.30")), direction);
            Assert.Empty(watch.Inspect(Interval));
            Assert.Empty(service.Inspect(Interval));

            var packet = Packet(direction, protocol);
            byte[] original = (byte[])packet.Bytes.Clone();
            watch.ReportNetworkTraffic(packet, direction);
            Assert.Equal(original, packet.Bytes);
            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(Interval)));
            Assert.Equal(4, usage.Bytes);
            Assert.Equal(4, Assert.IsType<NetworkServiceUsage>(Assert.Single(usage.Tokens)).Bytes);
        }

        [Theory]
        [InlineData(PacketDirection.Inbound, false)]
        [InlineData(PacketDirection.Outbound, false)]
        [InlineData(PacketDirection.Inbound, true)]
        [InlineData(PacketDirection.Outbound, true)]
        public void SMBClientKeepalive_DoesNotCountAsWatchedHostOrServiceUsage(PacketDirection direction, bool ipv6)
        {
            var watch = CreateWatch();
            var service = AddService(watch, IPProtocol.TCP, new TCPServiceFilterRule(445) { Type = FilterRuleType.Must });

            // jindujun:50000 <-> DRAGON:445 never involves jindujun's service port.
            var packet = Packet(direction, watchedPort: 50000, ipv6: ipv6);
            watch.ReportNetworkTraffic(packet, direction);
            packet.Extract<TcpPacket>()!.PayloadData = []; // pure ACK must not count as activity either
            watch.ReportNetworkTraffic(packet, direction);

            Assert.Empty(watch.Inspect(Interval));
            Assert.Empty(service.Inspect(Interval));
        }

        [Theory]
        [InlineData(PacketDirection.Inbound)]
        [InlineData(PacketDirection.Outbound)]
        public void HostRules_CanAllowUnmatchedTrafficButMustNotStillVetoesIt(PacketDirection direction)
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

            var packet = Packet(direction, watchedPort: 50000);
            packet.Extract<TcpPacket>()!.PayloadData = [];
            watch.ReportNetworkTraffic(packet, direction);
            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(Interval)));
            Assert.Equal(0, usage.Bytes); // accepted ACK is activity even without payload
            Assert.Empty(usage.Tokens);
            Assert.Empty(service.Inspect(Interval));
        }

        [Theory]
        [InlineData(PacketDirection.Inbound)]
        [InlineData(PacketDirection.Outbound)]
        public void MissingPayload_DoesNotCountOrStartARequest(PacketDirection direction)
        {
            var watch = CreateWatch();
            var service = AddService(watch, IPProtocol.TCP, new PayloadFilterRule { Type = FilterRuleType.Must });
            var packet = Packet(direction);

            Assert.Throws<ServicePayloadNeededException>(() => watch.Verify(packet, direction));
            if (direction == PacketDirection.Inbound)
                Assert.Null(watch.Evaluate(new CaptureSummary(packet)));
            else
                watch.ReportNetworkTraffic(packet, direction);

            Assert.Empty(watch.Inspect(Interval));
            Assert.Empty(service.Inspect(Interval));
        }

        [Fact]
        public void DemandEventAccounting_FiltersEachPacket()
        {
            var watch = CreateWatch();
            AddService(watch, IPProtocol.TCP, new TCPServiceFilterRule(445) { Type = FilterRuleType.Must });
            watch.Report(new DemandEvent(watch.Host, packets:
                [Packet(PacketDirection.Inbound), Packet(PacketDirection.Inbound, watchedPort: 50000)]));

            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(watch.Inspect(Interval)));
            Assert.Equal(4, usage.Bytes);
            Assert.Equal(4, Assert.IsType<NetworkServiceUsage>(Assert.Single(usage.Tokens)).Bytes);
        }

        [Theory]
        [InlineData(PacketDirection.Inbound, false)]
        [InlineData(PacketDirection.Outbound, false)]
        [InlineData(PacketDirection.Inbound, true)]
        [InlineData(PacketDirection.Outbound, true)]
        public void RouterPolicy_UsesThePeerAddress(PacketDirection direction, bool allowWake)
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
            IPacketFilter filter = new RouterFilter
            {
                Logger = NullLogger<RouterFilter>.Instance,
                Network = watch.Network,
                Reachability = null! // unicast accounting must not probe VPN clients
            };

            Assert.Equal(!allowWake, filter.ShouldFilter(Packet(direction), direction: direction));
            Assert.False(filter.ShouldFilter(Packet(direction, peer: IPAddress.Parse("192.0.2.30")), direction: direction));
        }
    }
}
