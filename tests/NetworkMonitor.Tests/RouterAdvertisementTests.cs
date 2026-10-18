using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Discovery.BuiltIn;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using SharpPcap;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class RouterAdvertisementTests
    {
        private static readonly PhysicalAddress Mac = PhysicalAddress.Parse("001122334455");
        private static readonly IPAddress Peer = IPAddress.IPv6Loopback; // reverse lookup is local
        private static readonly IPAddress Additional = IPAddress.Parse("fe80::2");

        [Theory]
        [InlineData(AutoDiscoveryType.Nothing)]
        [InlineData(AutoDiscoveryType.Router)]
        public void KnownRoutersAreUpdatedSynchronouslyWithoutAnActiveDiscovery(AutoDiscoveryType auto)
        {
            using var fixture = new Fixture(auto);
            var router = new NetworkRouter("known") { Network = fixture.Network, Options = new(), VPNClients = [] };
            router.AddAddress(Peer);
            fixture.Network.AddHost(router);
            var before = DateTime.Now;

            fixture.Process(Peer, 60);
            fixture.Process(Additional, 120);

            Assert.Equal(Mac, router.PhysicalAddress);
            Assert.True(router.HasAddress(ip: Additional));
            Assert.True(router.ValidUntil >= before.AddSeconds(120));
            Assert.True(router.ShouldAddressExpire(Additional, out var expires));
            Assert.True(expires >= before.AddSeconds(120));
        }

        [Fact]
        public void UnknownRoutersAreIgnoredOutsideDiscovery()
        {
            using var fixture = new Fixture();
            fixture.Process(Peer, 60);
            Assert.Empty(fixture.Network);
        }

        [Fact]
        public async Task DiscoveryCollectsTheWholeWindowAndCoalescesAddressesAndLifetimes()
        {
            using var fixture = new Fixture();
            DateTime received = default;
            fixture.Solicitation.OnSend = () =>
            {
                fixture.Process(Peer, 120);
                fixture.Process(Additional, 60); // the latest advertisement may shorten the lifetime
                received = DateTime.Now;
                Assert.Empty(fixture.Network); // the packet callback must only enqueue candidates
            };
            var elapsed = Stopwatch.StartNew();
            var contexts = await fixture.Discover();

            var router = Assert.IsType<NetworkRouter>(Assert.Single(contexts).Host);
            Assert.True(elapsed.Elapsed >= fixture.Timeout);
            Assert.Equal(2, router.IPAddresses.Count());
            Assert.Contains(Peer, router.IPAddresses);
            Assert.Contains(Additional, router.IPAddresses);
            Assert.True(router.ValidUntil <= received.AddSeconds(60));
            Assert.True(router.ValidUntil > received.AddSeconds(59));

            fixture.Process(IPAddress.Parse("fe80::3"), 120);
            Assert.True(router.HasAddress(ip: IPAddress.Parse("fe80::3")));
            fixture.Process(IPAddress.Parse("fe80::4"), 60, PhysicalAddress.Parse("001122334466"));
            Assert.Single(fixture.Network); // discovery no longer creates routers after the window
        }

        [Fact]
        public async Task AWithdrawalSupersedesAnEarlierQueuedLifetime()
        {
            using var fixture = new Fixture();
            fixture.Solicitation.OnSend = () =>
            {
                fixture.Process(Peer, 60);
                fixture.Process(Peer, 0);
            };

            Assert.Empty(await fixture.Discover());
            Assert.Empty(fixture.Network);
        }

        [Fact]
        public async Task AQueuedCandidateDoesNotRecreateOrOverwriteAKnownRouter()
        {
            using var fixture = new Fixture();
            var router = new NetworkRouter("known") { Network = fixture.Network, Options = new(), VPNClients = [], PhysicalAddress = Mac };
            fixture.Solicitation.OnSend = () =>
            {
                fixture.Process(Peer, 10);
                fixture.Network.AddHost(router);
                fixture.Process(Peer, 120);
            };

            Assert.Empty(await fixture.Discover());
            Assert.Same(router, Assert.Single(fixture.Network));
            Assert.True(router.ValidUntil > DateTime.Now.AddSeconds(110));
        }

        [Fact]
        public async Task FailedSolicitationDetachesTheChannelAndAllowsAnotherPass()
        {
            using var fixture = new Fixture();
            fixture.Solicitation.OnSend = () => throw new InvalidOperationException("send failed");
            await Assert.ThrowsAsync<InvalidOperationException>(fixture.Discover);
            fixture.Process(Peer, 60);
            Assert.Empty(fixture.Network);

            fixture.Solicitation.OnSend = () => fixture.Process(Peer, 0);
            Assert.Empty(await fixture.Discover());
        }

        [Fact]
        public async Task CreationDoesNotAcquireTheNetworkMutexOrDiscoverAddresses()
        {
            using var fixture = new Fixture();
            var expires = DateTime.Now.AddMinutes(1);

            NetworkRouterContext context;
            using (await fixture.Network.Mutex.LockAsync())
            {
                context = await Task.Run(() => fixture.Context.CreateDynamicRouter<NetworkRouterContext>(new()
                {
                    Name = "dynamic", MAC = Mac, IPv6 = Peer, AutoDetect = AutoDiscoveryType.IPv4
                }, expires)).WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.Equal(expires, ((NetworkRouter)context.Host).ValidUntil);
            Assert.Equal(0, fixture.AddressDiscovery.Calls);
        }

        [Fact]
        public async Task OuterDiscoveryDiscoversAddressesForConfiguredAndYieldedRouters()
        {
            using var fixture = new Fixture(AutoDiscoveryType.Router | AutoDiscoveryType.IPv4);
            fixture.Config.Router.Add(new NetworkRouterInfo { Name = "configured", AutoDetect = AutoDiscoveryType.IPv4 });
            fixture.Solicitation.OnSend = () => fixture.Process(Peer, 60);

            await fixture.Context.DiscoverRouters();

            Assert.Equal(2, fixture.Network.Count());
            Assert.Equal(2, fixture.AddressDiscovery.Calls);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly IContainer _container;
            private readonly List<NetworkHostContext> _contexts = [];
            public TimeSpan Timeout { get; } = TimeSpan.FromMilliseconds(100);
            public NetworkMonitorConfig Config { get; } = new() { AutoDetect = AutoDiscoveryType.Router };
            public NetworkSegment Network { get; }
            public NetworkDevice Device { get; }
            public NetworkContext Context { get; }
            public RouterAdvertismentDetector Detector { get; }
            public SolicitationFilter Solicitation { get; } = new();
            public AddressDiscovery AddressDiscovery { get; } = new();

            public Fixture(AutoDiscoveryType auto = AutoDiscoveryType.Router)
            {
                var adapter = new FakeNetworkInterface("test")
                {
                    PhysicalAddress = PhysicalAddress.Parse("AABBCCDDEEFF"),
                    Addresses = [new AddressInfo(IPAddress.Parse("fe80::1"))]
                };
                var live = DispatchProxy.Create<ILiveDevice, ReconfigurationLifecycleTests.NativeDevice>();
                Device = new(NullLogger<NetworkDevice>.Instance, adapter, live) { Filters = [Solicitation] };
                Network = new() { Logger = NullLogger<NetworkSegment>.Instance, Device = Device, LocalRange = null!, Ranges = null! };
                Detector = new(auto, new() { Timeout = Timeout })
                {
                    Logger = NullLogger<RouterAdvertismentDetector>.Instance, Device = Device, Network = Network
                };
                var builder = new ContainerBuilder();
                builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
                builder.RegisterInstance(Network);
                builder.RegisterInstance(Detector).As<IRouterDiscovery>();
                builder.RegisterInstance(AddressDiscovery).As<IIPAddressDiscovery>();
                builder.RegisterType<NetworkRouterContext>().ExternallyOwned();
                builder.RegisterType<NetworkJanitor>().SingleInstance();
                _container = builder.Build();
                Context = (NetworkContext)RuntimeHelpers.GetUninitializedObject(typeof(NetworkContext));
                typeof(NetworkContext).GetProperty("Config")!.SetValue(Context, Config);
                typeof(NetworkContext).GetField("_hostContexts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Context, _contexts);
                typeof(MadWizard.Desomnia.Network.Context.Context).GetProperty("Scope", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Context, _container);
            }

            public void Process(IPAddress ip, ushort lifetime, PhysicalAddress? mac = null)
            {
                // Raw ICMPv6 Router Advertisement header, parsed like a captured packet.
                byte[] data = [134, 0, 0, 0, 64, 0, (byte)(lifetime >> 8), (byte)lifetime, 0, 0, 0, 0, 0, 0, 0, 0];
                var ipv6 = new IPv6Packet(ip, IPAddress.Parse("ff02::1"))
                {
                    Protocol = PacketDotNet.ProtocolType.IcmpV6, HopLimit = 255, PayloadData = data, PayloadLength = (ushort)data.Length
                };
                var ethernet = new EthernetPacket(mac ?? Mac, PhysicalAddress.Parse("333300000001"), EthernetType.IPv6) { PayloadPacket = ipv6 };
                var parsed = (EthernetPacket)Packet.ParsePacket(LinkLayers.Ethernet, ethernet.Bytes);
                ((INetworkService)Detector).ProcessPacket(parsed);
            }

            public async Task<List<NetworkRouterContext>> Discover()
            {
                List<NetworkRouterContext> contexts = [];
                await foreach (var context in ((IRouterDiscovery)Detector).DiscoverRouters(Context))
                    contexts.Add(context);
                return contexts;
            }

            public void Dispose()
            {
                foreach (var context in _contexts.ToArray()) context.Dispose();
                _container.Dispose();
                ((IDisposable)Device).Dispose();
            }
        }

        private sealed class SolicitationFilter : IDevicePacketFilter
        {
            public Action? OnSend;
            public bool FilterIncoming(PacketCapture packet) => false;
            public bool FilterOutgoing(Packet packet) { OnSend?.Invoke(); return true; }
        }

        private sealed class AddressDiscovery : IIPAddressDiscovery
        {
            public int Calls;
            public Task DiscoverIPAddresses(NetworkHost host, AddressFamily family) { Calls++; return Task.CompletedTask; }
        }

        private sealed class AddressInfo(IPAddress address) : UnicastIPAddressInformation
        {
            public override IPAddress Address => address;
            public override bool IsDnsEligible => false;
            public override bool IsTransient => false;
            public override long AddressPreferredLifetime => long.MaxValue;
            public override long AddressValidLifetime => long.MaxValue;
            public override long DhcpLeaseLifetime => long.MaxValue;
            public override DuplicateAddressDetectionState DuplicateAddressDetectionState => DuplicateAddressDetectionState.Preferred;
            public override IPAddress IPv4Mask => IPAddress.Any;
            public override PrefixOrigin PrefixOrigin => PrefixOrigin.Manual;
            public override SuffixOrigin SuffixOrigin => SuffixOrigin.Manual;
        }
    }
}
