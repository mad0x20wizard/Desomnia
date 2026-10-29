using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging.Abstractions;
using NetTools;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Firewall.Tests
{
    public class FirewallTests
    {
        private static readonly IPAddress Target = IPAddress.Parse("192.0.2.100");
        private static readonly IPService Service = new(IPProtocol.TCP, 8443);

        [Fact]
        public void MustAddsServiceFallbackAndSourcePermits()
        {
            var rules = Compile(new TCPServiceFilterRule(8443)
            {
                Type = FilterRuleType.Must,
                HostRules = [Host(FilterRuleType.Must, "192.0.2.10")]
            });
            Assert.Equal(2, rules.Length);
            Assert.Equal(FirewallAction.Permit, Decide(rules, "192.0.2.10"));
            Assert.Equal(FirewallAction.Block, Decide(rules, "192.0.2.11"));
            Assert.All(rules, r => { Assert.Equal(Target, r.LocalAddress); Assert.Equal(Service, r.Service); });
        }

        [Fact]
        public void MustNotOnlyDoesNotIntroduceFallbackBlock()
        {
            var rules = Compile(new TCPServiceFilterRule(8443)
            {
                Type = FilterRuleType.MustNot,
                HostRules = [Host(FilterRuleType.Must, "192.0.2.10")]
            });
            Assert.Single(rules);
            Assert.Equal(FirewallAction.Block, Decide(rules, "192.0.2.10"));
            Assert.Null(Decide(rules, "192.0.2.11"));
        }

        [Fact]
        public void NestedExclusionDoesNotBecomeAGlobalDeny()
        {
            var rules = Compile(
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Host(FilterRuleType.MustNot, "192.0.2.10")]
                },
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Host(FilterRuleType.Must, "192.0.2.10")]
                });
            Assert.Equal(FirewallAction.Permit, Decide(rules, "192.0.2.10"));
        }

        [Fact]
        public void ProjectionAgreesWithPacketFilterAcrossRangeBoundaries()
        {
            PacketFilterRule[] configured =
            [
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules =
                    [
                        new StaticHostRangeFilterRule { Type = FilterRuleType.Must, Range = IPAddressRange.Parse("192.0.2.8/29") },
                        Host(FilterRuleType.MustNot, "192.0.2.10")
                    ]
                },
                new TCPServiceFilterRule(8443) { Type = FilterRuleType.Must, HostRules = [Host(FilterRuleType.Must, "192.0.2.20")] },
                new TCPServiceFilterRule(8443) { Type = FilterRuleType.MustNot, HostRules = [Host(FilterRuleType.Must, "192.0.2.12")] }
            ];
            var rules = Compile(configured);
            var filter = new RuleFilter(configured);
            for (int i = 0; i < 32; i++)
            {
                var source = IPAddress.Parse($"192.0.2.{i}");
                var packet = new EthernetPacket(PhysicalAddress.Parse("020000000001"), PhysicalAddress.Parse("020000000002"), EthernetType.IPv4)
                {
                    PayloadPacket = new IPv4Packet(source, Target) { PayloadPacket = new TcpPacket(50000, 8443) }
                };
                Assert.Equal(!filter.ShouldFilter(new EthernetCapture(packet), default), Decide(rules, source.ToString()) == FirewallAction.Permit);
            }
        }

        [Fact]
        public void IPv6AndCombinedProtocolsRemainSeparate()
        {
            var target = IPAddress.Parse("2001:db8::100");
            var rules = FirewallRuleCompiler.Compile([target], new(IPProtocol.TCP | IPProtocol.UDP, 53),
            [
                new TCPServiceFilterRule(53) { Type = FilterRuleType.Must, HostRules = [Host(FilterRuleType.Must, "2001:db8::10")] },
                new UDPServiceFilterRule(53) { Type = FilterRuleType.MustNot }
            ]);
            Assert.Contains(rules, r => r.Service.Protocol == IPProtocol.TCP && r.Action == FirewallAction.Permit && r.Source.Contains(IPAddress.Parse("2001:db8::10")));
            Assert.Contains(rules, r => r.Service.Protocol == IPProtocol.UDP && r.Priority == FirewallPriority.Deny);
            Assert.DoesNotContain(rules, r => r.Source.Begin.AddressFamily != target.AddressFamily);
        }

        [Fact]
        public void DynamicSourcesAreRejectedInsteadOfInstalledAsStaleAuthorization()
        {
            using var range = (IDisposable)new NetworkHostRange();
            Assert.Throws<NotSupportedException>(() => Compile(new TCPServiceFilterRule(8443)
            {
                Type = FilterRuleType.Must,
                HostRules = [new DynamicHostRangeFilterRule { Type = FilterRuleType.Must, Range = (NetworkHostRange)range }]
            }));
        }

        [Fact]
        public async Task StartupInstallsOnlyStaticPhysicalLocalServicesAndShutdownReleasesThem()
        {
            var network = new NetworkSegment { Device = null!, Logger = NullLogger<NetworkSegment>.Instance, LocalRange = null!, Ranges = null! };
            using var monitor = new NetworkMonitor { Name = "test", Options = default, Device = null!, Network = network, Janitor = null!, Pipeline = null!, Logger = NullLogger<NetworkMonitor>.Instance };
            var host = new TestLocalHost { Network = network };
            var watch = CreateWatch(host);
            monitor.StartTracking(watch, adopt: false);
            AddService(watch, 8443, ServiceFlags.Static);
            AddService(watch, 8444, default);
            var virtualHost = new VirtualNetworkHost("guest") { Network = network, PhysicalHost = host };
            var virtualWatch = CreateWatch(virtualHost);
            monitor.StartTracking(virtualWatch, adopt: false);
            AddService(virtualWatch, 8445, ServiceFlags.Static);

            var backend = new RecordingFirewall();
            using var firewall = new FirewallMonitor { Monitor = monitor, Firewall = backend, Logger = NullLogger<FirewallMonitor>.Instance };
            INetworkService service = firewall;
            await service.Startup();
            Assert.Empty(backend.Rules); // filters are not resolved before discovery
            await service.AfterStartup();
            Assert.All(backend.Rules, r => Assert.Equal((ushort)8443, r.Service.Port));
            Assert.Equal(2, backend.Rules.Count);
            await service.Shutdown(NetworkShutdownReason.InterfaceDisconnected);
            Assert.True(backend.Released);
        }

        [Theory]
        [InlineData("192.0.2.100", "192.168.128.52", "0.0.0.0", "255.255.255.255")]
        [InlineData("2001:db8::100", "2001:db8::52", "::", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
        [InlineData("192.0.2.100", "0.0.0.0", "0.0.0.0", "255.255.255.255")]
        [InlineData("192.0.2.100", "255.255.255.255", "0.0.0.0", "255.255.255.255")]
        [InlineData("2001:db8::100", "::", "::", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
        [InlineData("2001:db8::100", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", "::", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
        public void SourceExclusionBecomesOneDirectBlock(string target, string excluded, string first, string last)
        {
            var rules = FirewallRuleCompiler.Compile([IPAddress.Parse(target)], new(IPProtocol.TCP, 22),
            [
                new TCPServiceFilterRule(22)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Host(FilterRuleType.MustNot, excluded)]
                }
            ]);
            Assert.Equal(3, rules.Length);
            var permit = Assert.Single(rules, r => r.Action == FirewallAction.Permit);
            Assert.Equal(IPAddress.Parse(first), permit.Source.Begin);
            Assert.Equal(IPAddress.Parse(last), permit.Source.End);
            var deny = Assert.Single(rules, r => r.Priority == FirewallPriority.Deny);
            Assert.Equal(IPAddress.Parse(excluded), deny.Source.Begin);
            Assert.Equal(deny.Source.Begin, deny.Source.End);
            Assert.Equal(FirewallAction.Block, Decide(rules, excluded));
        }

        [Fact]
        public void IPv4ExclusionDoesNotExcludeIPv6Sources()
        {
            var target6 = IPAddress.Parse("2001:db8::100");
            var rules = FirewallRuleCompiler.Compile([Target, target6], Service,
            [
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Host(FilterRuleType.MustNot, "192.0.2.10")]
                }
            ]);
            Assert.Equal(Target, Assert.Single(rules, r => r.Priority == FirewallPriority.Deny).LocalAddress);
            Assert.Equal(FirewallAction.Permit, Decide(rules.Where(r => r.LocalAddress.Equals(target6)), "2001:db8::10"));
            Assert.All(rules, r => Assert.Equal(r.LocalAddress.AddressFamily, r.Source.Begin.AddressFamily));
        }

        [Fact]
        public void OverlappingAlternativeShrinksTheEffectiveExclusion()
        {
            var rules = Compile(
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules =
                    [
                        Range(FilterRuleType.Must, "192.0.2.0", "192.0.2.255"),
                        Range(FilterRuleType.MustNot, "192.0.2.40", "192.0.2.80")
                    ]
                },
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Range(FilterRuleType.Must, "192.0.2.60", "192.0.2.100")]
                });
            var permit = Assert.Single(rules, r => r.Action == FirewallAction.Permit);
            Assert.Equal(IPAddress.Parse("192.0.2.0"), permit.Source.Begin);
            Assert.Equal(IPAddress.Parse("192.0.2.255"), permit.Source.End);
            var deny = Assert.Single(rules, r => r.Priority == FirewallPriority.Deny);
            Assert.Equal(IPAddress.Parse("192.0.2.40"), deny.Source.Begin);
            Assert.Equal(IPAddress.Parse("192.0.2.59"), deny.Source.End);
            Assert.Equal(FirewallAction.Permit, Decide(rules, "192.0.2.60"));
            Assert.Equal(FirewallAction.Block, Decide(rules, "192.0.3.1"));
        }

        [Fact]
        public void AlternativeExclusionsBlockOnlyTheirIntersection()
        {
            var rules = Compile(
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Range(FilterRuleType.MustNot, "192.0.2.40", "192.0.2.80")]
                },
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Range(FilterRuleType.MustNot, "192.0.2.60", "192.0.2.100")]
                });
            var deny = Assert.Single(rules, r => r.Priority == FirewallPriority.Deny);
            Assert.Equal(IPAddress.Parse("192.0.2.60"), deny.Source.Begin);
            Assert.Equal(IPAddress.Parse("192.0.2.80"), deny.Source.End);
            Assert.Equal(FirewallAction.Permit, Decide(rules, "192.0.2.40"));
            Assert.Equal(FirewallAction.Permit, Decide(rules, "192.0.2.100"));
        }

        [Fact]
        public void IndependentHostPermitRescuesNestedExclusionButCannotOverrideIndependentDeny()
        {
            var rules = Compile(
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.Must,
                    HostRules = [Host(FilterRuleType.MustNot, "192.0.2.10")]
                },
                Host(FilterRuleType.Must, "192.0.2.10"),
                Range(FilterRuleType.MustNot, "192.0.2.20", "192.0.2.30"));
            Assert.Equal(FirewallAction.Permit, Decide(rules, "192.0.2.10"));
            Assert.Equal(FirewallAction.Block, Decide(rules, "192.0.2.20"));
            Assert.Equal(FirewallAction.Block, Decide(rules, "192.0.2.30"));
        }

        [Fact]
        public void ExceptionToOneDenyDoesNotPermitThroughAnotherDeny()
        {
            var rules = Compile(
                new TCPServiceFilterRule(8443)
                {
                    Type = FilterRuleType.MustNot,
                    HostRules =
                    [
                        Range(FilterRuleType.Must, "192.0.2.0", "192.0.2.255"),
                        Host(FilterRuleType.MustNot, "192.0.2.10")
                    ]
                },
                Host(FilterRuleType.MustNot, "192.0.2.10"));
            Assert.Single(rules);
            Assert.DoesNotContain(rules, r => r.Action == FirewallAction.Permit);
            Assert.Equal(FirewallAction.Block, Decide(rules, "192.0.2.10"));
            Assert.Null(Decide(rules, "192.0.3.1"));
        }

        [Fact]
        public void MustForAnotherServiceStillRequiresFallbackWithoutPermits()
        {
            var rules = Compile(new UDPServiceFilterRule(53) { Type = FilterRuleType.Must });
            Assert.Equal(FirewallPriority.Default, Assert.Single(rules).Priority);
        }

        [Theory]
        [InlineData(false, IPProtocol.TCP)]
        [InlineData(false, IPProtocol.UDP)]
        [InlineData(true, IPProtocol.TCP)]
        [InlineData(true, IPProtocol.UDP)]
        public void CombinedProjectionAgreesWithPacketFilter(bool ipv6, IPProtocol protocol)
        {
            var random = new Random(1753);
            IPAddress Address(int value) => IPAddress.Parse(ipv6 ? $"2001:db8::{value:x}" : $"192.0.2.{value}");
            StaticHostRangeFilterRule NextRange(FilterRuleType type)
            {
                int first = random.Next(0, 32), last = random.Next(first, 32);
                return new() { Type = type, Range = new(Address(first), Address(last)) };
            }
            var target = Address(100);
            var service = new IPService(protocol, 8443);
            for (int sample = 0; sample < 60; sample++)
            {
                List<PacketFilterRule> configured = [];
                for (int i = 0; i < sample % 6; i++)
                {
                    var type = (FilterRuleType)random.Next(2);
                    if (random.Next(3) == 0)
                        configured.Add(NextRange(type));
                    else
                    {
                        HostFilterRule[] hosts = [.. Enumerable.Range(0, random.Next(5))
                            .Select(_ => NextRange((FilterRuleType)random.Next(2)))];
                        configured.Add(random.Next(2) == 0
                            ? new TCPServiceFilterRule(8443) { Type = type, HostRules = hosts }
                            : new UDPServiceFilterRule(8443) { Type = type, HostRules = hosts });
                    }
                }
                var projected = FirewallRuleCompiler.Compile([target], service, configured);
                var filter = new RuleFilter(configured);
                for (int i = 0; i < 34; i++)
                {
                    var source = Address(i);
                    IPPacket ip = ipv6 ? new IPv6Packet(source, target) : new IPv4Packet(source, target);
                    ip.PayloadPacket = protocol == IPProtocol.TCP ? new TcpPacket(50000, 8443) : new UdpPacket(50000, 8443);
                    var packet = new EthernetPacket(PhysicalAddress.Parse("020000000001"),
                        PhysicalAddress.Parse("020000000002"), ipv6 ? EthernetType.IPv6 : EthernetType.IPv4) { PayloadPacket = ip };
                    Assert.Equal(filter.ShouldFilter(new EthernetCapture(packet), default), Decide(projected, source.ToString()) == FirewallAction.Block);
                }
            }
        }

        private static StaticHostRangeFilterRule Range(FilterRuleType type, string first, string last) =>
            new() { Type = type, Range = new(IPAddress.Parse(first), IPAddress.Parse(last)) };

        private sealed class TestLocalHost() : LocalHost(null!)
        {
            public override IEnumerable<IPAddress> IPAddresses => [Target];
        }

        private sealed class TestWatch : HostDemandWatch { public override bool IsOnline => true; }

        private static TestWatch CreateWatch(NetworkHost host) => new()
        {
            Host = host, Network = host.Network, Logger = NullLogger<NetworkHostWatch>.Instance,
            Scope = null!, Device = null!, AdvertiseOptions = default, HandoffOptions = default,
            DemandOptions = default, DefaultFilterOptions = default,
            Filter = new(() => new RuleFilter([]))
        };

        private static void AddService(TestWatch host, ushort port, ServiceFlags flags)
        {
            var service = new TransportNetworkService("test", new(IPProtocol.TCP, port));
            host.Host.AddService(service, new(flags));
            host.StartTracking(new NetworkServiceWatch(service)
            {
                Filter = new(() => new RuleFilter([new TCPServiceFilterRule(port) { Type = FilterRuleType.Must }]))
            }, adopt: false);
        }

        private sealed class RecordingFirewall : IFirewallManager
        {
            internal List<FirewallRule> Rules = [];
            internal bool Released;
            public IDisposable Install(NetworkHost host, IReadOnlyCollection<FirewallRule> rules)
            {
                Assert.IsAssignableFrom<LocalHost>(host);
                Rules.AddRange(rules);
                return new Release(this);
            }
            private sealed class Release(RecordingFirewall owner) : IDisposable { public void Dispose() => owner.Released = true; }
        }

        private static StaticHostFilterRule Host(FilterRuleType type, string ip) => new() { Type = type, Addresses = [IPAddress.Parse(ip)] };
        private static FirewallRule[] Compile(params PacketFilterRule[] rules) => FirewallRuleCompiler.Compile([Target], Service, rules);
        private static FirewallAction? Decide(IEnumerable<FirewallRule> rules, string source) =>
            rules.Where(r => r.Source.Contains(IPAddress.Parse(source))).OrderByDescending(r => r.Priority).FirstOrDefault()?.Action;
    }
}
