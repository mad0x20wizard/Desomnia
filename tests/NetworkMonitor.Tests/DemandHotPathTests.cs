using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Demand.Detector;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

using Autofac;
using System.Text;

namespace MadWizard.Desomnia.Network.Tests
{
    public class DemandHotPathTests
    {
        private static readonly PhysicalAddress SourceMAC = PhysicalAddress.Parse("001122334455");
        private static readonly PhysicalAddress TargetMAC = PhysicalAddress.Parse("AABBCCDDEEFF");
        private static readonly IPAddress SourceIP = IPAddress.Parse("192.0.2.10");
        private static readonly IPAddress TargetIP = IPAddress.Parse("192.0.2.20");

        private sealed class TestHostWatch : NetworkHostWatch { }

        private sealed class TestDemandWatch : HostDemandWatch
        {
            public bool Online { get; init; } = true;

            public override bool IsOnline => Online;
        }

        private sealed class RoutingWatch : LocalHostWatch
        {
            public override bool IsOnline => false;
            public int DemandEvaluations { get; private set; }
            private protected override bool ShouldStartRequest(INetworkCapture capture)
            {
                DemandEvaluations++;
                return false;
            }
        }

        private static NetworkSegment CreateNetwork() => new()
        {
            Logger = NullLogger<NetworkSegment>.Instance,
            Device = null!,
            LocalRange = null!,
            Ranges = null!
        };

        private static EthernetPacket CreateTCPPacket(bool synchronize, byte[] payload)
        {
            var tcp = new TcpPacket(50000, 445)
            {
                Synchronize = synchronize,
                PayloadData = payload
            };

            return new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv4)
            {
                PayloadPacket = new IPv4Packet(SourceIP, TargetIP)
                {
                    PayloadPacket = tcp
                }
            };
        }

        [Fact]
        public void Capture_ExtractsTransportMetadataAndPayloadLengthOnce()
        {
            byte[] payload = [1, 2, 3, 4, 5];

            var summary = new EthernetCapture(CreateTCPPacket(synchronize: false, payload));

            Assert.Equal(SourceIP, summary.SourceAddress);
            Assert.Equal(TargetIP, summary.TargetAddress);
            Assert.Equal(new IPService(IPProtocol.TCP, 50000), (IPService)Assert.IsType<IPServiceEndPoint>(summary.SourceEndPoint));
            Assert.Equal(new IPService(IPProtocol.TCP, 445), (IPService)Assert.IsType<IPServiceEndPoint>(summary.TargetEndPoint));
            Assert.Equal(payload.Length, summary.PayloadLength);
            Assert.False(summary.FindPacket<TcpPacket>()!.Synchronize);
        }

        [Fact]
        public void Capture_CountsDecodedTransportPayloadWithoutPayloadDataCopy()
        {
            var wakeOnLan = new WakeOnLanPacket(TargetMAC);
            var udp = new UdpPacket(50000, 9) { PayloadPacket = wakeOnLan };
            var ip = new IPv4Packet(SourceIP, TargetIP)
            {
                Protocol = ProtocolType.Udp,
                PayloadPacket = udp
            };
            var packet = new EthernetPacket(SourceMAC, TargetMAC, EthernetType.IPv4)
            {
                PayloadPacket = ip
            };

            INetworkCapture summary = new EthernetCapture(packet);

            Assert.Equal(wakeOnLan.TotalPacketLength, summary.PayloadLength);
            Assert.True(summary.IsMagicPacket());
        }

        [Fact]
        public void NetworkSegment_IndexesFollowAddressChanges()
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target")
            {
                Network = network,
                PhysicalAddress = TargetMAC
            };

            host.AddAddress(TargetIP);
            network.AddHost(host);

            Assert.Same(host, network[TargetIP]);
            Assert.Same(host, network[TargetMAC]);

            var replacementMAC = PhysicalAddress.Parse("AABBCCDDEE00");
            host.PhysicalAddress = replacementMAC;
            host.RemoveAddress(TargetIP);

            Assert.Null(network[TargetIP]);
            Assert.Null(network[TargetMAC]);
            Assert.Same(host, network[replacementMAC]);

            network.RemoveHost(host);

            Assert.Null(network[replacementMAC]);
        }

        [Fact]
        public void MonitorIndexesFollowHostAndServiceTracking()
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target") { Network = network };
            var hostWatch = new TestHostWatch
            {
                Host = host,
                Logger = NullLogger<NetworkHostWatch>.Instance
            };
            var serviceWatch = new NetworkServiceWatch(
                new NetworkService("SMB", new(IPProtocol.TCP, 445)))
                { Filter = new(() => new RuleFilter([])) };
            var monitor = new NetworkMonitor
            {
                Logger = NullLogger<NetworkMonitor>.Instance,
                Name = "test",
                Options = default,
                Device = null!,
                Network = network,
                Pipeline = new CapturePipeline([]) { Logger = NullLogger<CapturePipeline>.Instance }, Janitor = null!
            };

            monitor.StartTracking(hostWatch, adopt: false);
            hostWatch.StartTracking(serviceWatch, adopt: false);

            Assert.Same(hostWatch, monitor[host]);
            Assert.Same(serviceWatch, hostWatch.FirstOrDefault(watch => watch.Service.Contains(new(IPProtocol.TCP, 445))));
            Assert.Same(serviceWatch, hostWatch[serviceWatch.Service]);

            hostWatch.StopTracking(serviceWatch);
            monitor.StopTracking(hostWatch);

            Assert.Null(monitor[host]);
            Assert.Null(hostWatch.FirstOrDefault(watch => watch.Service.Contains(new(IPProtocol.TCP, 445))));
            Assert.Null(hostWatch[serviceWatch.Service]);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void EstablishedTCP_UsesDemandFilterWithoutStartingRequest(bool online, bool allowed)
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target") { Network = network };
            bool filterWasResolved = false;

            var watch = new TestDemandWatch
            {
                Host = host,
                Online = online,
                Logger = NullLogger<NetworkHostWatch>.Instance,
                Scope = null!,
                Device = null!,
                Network = network,
                AdvertiseOptions = default,
                HandoffOptions = default,
                DemandOptions = new DemandOptions { Source = DemandSource.IP, Parallel = 1 },
                Filter = new Lazy<IFilter>(() =>
                {
                    filterWasResolved = true;
                    return new RuleFilter(allowed ? [] : [new StaticHostFilterRule
                    {
                        Type = FilterRuleType.MustNot,
                        Addresses = [SourceIP]
                    }]);
                }),
                Parser = new DefaultFilterContextParser()
            };

            var summary = new EthernetCapture(CreateTCPPacket(synchronize: false, [1, 2, 3, 4]));

            Assert.Null(watch.Evaluate(summary));
            Assert.True(filterWasResolved);

            var tokens = watch.Inspect(TimeSpan.FromSeconds(1));
            if (allowed)
            {
                var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(tokens));
                Assert.Equal(4, usage.Bytes);
            }
            else
            {
                Assert.Empty(tokens);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutboundTrafficOnlyCountsForSourceAndDemandRoutesToDestination(bool destinationWatched)
        {
            var network = CreateNetwork();
            RoutingWatch Watch(string name, IPAddress address, PhysicalAddress mac)
            {
                var host = new NetworkHost(name) { Network = network, PhysicalAddress = mac };
                host.AddAddress(address);
                network.AddHost(host);
                return new RoutingWatch
                {
                    Host = host, Network = network, Device = null!, Scope = null!,
                    Logger = NullLogger<NetworkHostWatch>.Instance,
                    AdvertiseOptions = default, HandoffOptions = default, DemandOptions = default,
                    Parser = new DefaultFilterContextParser(),
                    AddressMapping = null!, MulticastDNS = null!, CreateSleepProxyRegistration = null!, CreateMessageBurst = null!,
                    Filter = new Lazy<IFilter>(() => new RuleFilter([]))
                };
            }
            var source = Watch("source", SourceIP, SourceMAC);
            var target = Watch("target", TargetIP, TargetMAC);
            var monitor = new NetworkMonitor
            {
                Logger = NullLogger<NetworkMonitor>.Instance, Name = "test",
                Options = new WatchOptions { Mode = WatchMode.Promiscuous },
                Device = null!, Network = network, Janitor = null!,
                Pipeline = new CapturePipeline([]) { Logger = NullLogger<CapturePipeline>.Instance }
            };
            monitor.StartTracking(source, adopt: false);
            if (destinationWatched) monitor.StartTracking(target, adopt: false);
            INetworkService service = new DemandService
            {
                Logger = NullLogger<DemandService>.Instance, Device = null!, Monitor = monitor,
                Network = network, AddressMapping = null!, Detectors = [new DemandByIP { Network = network }],
                Parser = new DefaultFilterContextParser()
            };

            service.ProcessCapture(new EthernetCapture(CreateTCPPacket(synchronize: true, [1, 2, 3, 4])));

            Assert.Equal(0, source.DemandEvaluations);
            Assert.Equal(destinationWatched ? 1 : 0, target.DemandEvaluations);
            var usage = Assert.IsType<NetworkHostUsage>(Assert.Single(source.Inspect(TimeSpan.FromSeconds(1))));
            Assert.Equal(4, usage.Bytes);
            Assert.Empty(target.Inspect(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void ServiceUsageHandlerDoesNotEnableExternalDemand()
        {
            var watch = new NetworkServiceWatch(
                new NetworkService("SMB", new(IPProtocol.TCP, 445)))
                { Filter = new(() => new RuleFilter([])) };
            var trigger = new EthernetCapture(CreateTCPPacket(synchronize: true, []));

            watch.Usage += _ => Task.CompletedTask;

            Assert.False(watch.CanTriggerDemand(trigger));

            watch.Demand += _ => Task.CompletedTask;

            Assert.True(watch.CanTriggerDemand(trigger));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IncompleteHttpOnlyStartsRequestOnTheDemandPath(bool synchronize)
        {
            var network = CreateNetwork();
            var host = new NetworkHost("target") { Network = network };
            var parser = new HTTPFilterContextParser();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(host);
            builder.RegisterInstance<IFilterContextParser>(parser);
            builder.RegisterType<DemandRequest>();
            using var root = builder.Build();
            var watch = new TestDemandWatch
            {
                Host = host, Network = network, Device = null!, Scope = root,
                Logger = NullLogger<NetworkHostWatch>.Instance,
                AdvertiseOptions = default, HandoffOptions = default,
                DemandOptions = new() { Source = DemandSource.IP, Parallel = 1 },
                Parser = parser, Filter = new(() => new RuleFilter([]))
            };
            var capture = new EthernetCapture(CreateTCPPacket(synchronize,
                Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: test\r\n")));
            var request = watch.Evaluate(capture);
            if (synchronize)
            {
                Assert.NotNull(request);
                Assert.Equal(1, request.Number);
                ((IDisposable)request).Dispose();
            }
            else
                Assert.Null(request);
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(1)));
        }
    }
}
