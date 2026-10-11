using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Services.Knocking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTools;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class KnockLifecycleTests
    {
        private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");

        [Fact]
        public async Task ContextRegistrationWorksAfterStartupAndDisposalRemovesOnlyItsStanzas()
        {
            using var fixture = new Fixture();
            using var firstRange = new NetworkHostRange();
            using var secondRange = new NetworkHostRange();
            await ((INetworkService)fixture.Service).Startup();
            using var first = fixture.Context.CreateKnockContext(Config("first", "one", "two"), firstRange);
            using var second = fixture.Context.CreateKnockContext(Config("second", "three"), secondRange);
            var firstStanzas = first.ToArray();
            Assert.Equal(2, firstStanzas.Length);
            Assert.Equal(2, fixture.ContextCount);

            fixture.Process();
            Assert.True(firstRange.Contains(Peer));
            Assert.True(secondRange.Contains(Peer));
            Assert.Equal(3, fixture.Detector.Examinations);

            first.Dispose();
            Assert.Equal(1, fixture.ContextCount);
            firstRange.RemoveAddress(Peer);
            secondRange.RemoveAddress(Peer);
            fixture.Process();
            foreach (var stanza in firstStanzas)
                stanza.TriggerKnockEvent(new(Peer, 50000), new(Peer)); // handlers also belong to the disposed scope

            Assert.False(firstRange.Contains(Peer));
            Assert.True(secondRange.Contains(Peer));
            Assert.Equal(4, fixture.Detector.Examinations);
            second.Dispose();
            Assert.Equal(0, fixture.ContextCount);
        }

        [Fact]
        public async Task RepeatedKnockExtendsTheGrant()
        {
            using var fixture = new Fixture();
            using var ranges = new NetworkHostRange();
            using var context = fixture.Context.CreateKnockContext(Config("renew", "one"), ranges);
            fixture.Process();
            var range = new IPAddressRange(Peer);
            var original = ranges[range].Expires;
            await Task.Delay(20);
            fixture.Process();

            Assert.True(ranges[range].Expires > original);
            Assert.Single(ranges);
        }

        [Fact]
        public void InvalidSecretDoesNotLeavePartOfAContextListening()
        {
            using var fixture = new Fixture();
            using var ranges = new NetworkHostRange();
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Context.CreateKnockContext(Config("invalid", "valid", "invalid"), ranges));

            fixture.Process();
            Assert.Equal(0, fixture.ContextCount);
            Assert.Equal(0, fixture.Detector.Examinations);
            Assert.Empty(ranges);

            using var valid = fixture.Context.CreateKnockContext(Config("valid", "valid"), ranges);
            fixture.Process();
            Assert.True(ranges.Contains(Peer));
        }

        [Fact]
        public void StanzaFiltersDoNotResolveUnrelatedParentRules()
        {
            using var fixture = new Fixture(unresolvableParentRule: true);
            using var ranges = new NetworkHostRange();
            using var context = fixture.Context.CreateKnockContext(Config("isolated", "one"), ranges);
            fixture.Process();
            Assert.True(ranges.Contains(Peer));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CallerCreatesDynamicFilterHostsAfterStanzaRegistration(bool allContexts)
        {
            using var fixture = new Fixture();
            using var ranges = new NetworkHostRange();
            var config = Config("filtered", "one");
            config.HostFilterRule.Add(new HostFilterRuleInfo { Name = "allowed", Type = FilterRuleType.Must });
            using var context = fixture.Context.CreateKnockContext(config, ranges);
            Assert.Null(fixture.Context.Network["allowed"]);
            Assert.Equal(1, fixture.ContextCount);

            Assert.False(Assert.Single(context).PacketFilter.IsValueCreated);
            Assert.Empty(ranges);
            Assert.Equal(0, fixture.Detector.Examinations);

            var created = allContexts
                ? fixture.Context.CreateDynamicFilterHosts()
                : fixture.Context.CreateDynamicFilterHosts(context);
            var host = Assert.Single(created).Host;
            Assert.Same(host, fixture.Context.Network["allowed"]);
            Assert.Equal(1, host.FilterRefCount);
            fixture.Process();
            Assert.Empty(ranges);

            host.AddAddress(Peer);
            fixture.Process();
            Assert.True(ranges.Contains(Peer));
            context.Dispose();
            Assert.Equal(0, host.FilterRefCount);
        }

        [Fact]
        public void FailedRegistrationDoesNotCreateDynamicFilterHosts()
        {
            using var fixture = new Fixture();
            using var ranges = new NetworkHostRange();
            var config = Config("filtered", "invalid");
            config.HostFilterRule.Add(new HostFilterRuleInfo { Name = "allowed", Type = FilterRuleType.Must });
            Assert.Throws<InvalidOperationException>(() => fixture.Context.CreateKnockContext(config, ranges));
            Assert.Null(fixture.Context.Network["allowed"]);
            Assert.Equal(0, fixture.ContextCount);
        }

        [Fact]
        public void IntermediateScopeOwnsAllStanzaScopesAndTrafficRequests()
        {
            using var fixture = new Fixture();
            using var ranges = new NetworkHostRange();
            using var context = fixture.Context.CreateKnockContext(Config("scope", "one", "two"), ranges);
            var scopes = (IList<ILifetimeScope>)typeof(NetworkKnockContext)
                .GetField("_scopes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
            int disposedRequests = 0;
            foreach (var scope in scopes)
            {
                scope.Resolve<TrafficFilterRequest>().Disposed += (_, _) => disposedRequests++;
                Assert.Equal(MatchingScopeLifetimeTags.KnockLifetimeScopeTag, scope.Tag);
            }

            context.Scope.Dispose();
            Assert.Equal(2, disposedRequests);
            Assert.Equal(0, fixture.ContextCount);
            fixture.Process();
            Assert.Equal(0, fixture.Detector.Examinations);
        }

        private static DynamicHostRangeInfo Config(string name, params string[] secrets) => new()
        {
            Name = name, KnockMethod = "test", KnockPort = 62201,
            KnockTimeout = TimeSpan.FromMinutes(1),
            SharedSecret = secrets.Select(secret => new SharedSecretData(secret)).ToList()
        };

        private sealed class Detector : IKnockDetector, IKnockValidation
        {
            public int Examinations;
            public void ValidateSecret(SharedSecret secret)
            {
                if (System.Text.Encoding.UTF8.GetString(secret.Key) == "invalid")
                    throw new InvalidOperationException("Invalid test secret");
            }
            public IEnumerable<KnockEvent> Examine(IPPacket packet, SharedSecret secret)
            {
                Examinations++;
                yield return new(packet.SourceAddress);
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly IContainer _container;
            private readonly List<NetworkKnockContext> _contexts = [];
            public Detector Detector { get; } = new();
            public KnockService Service { get; }
            public NetworkContext Context { get; }
            public int ContextCount => _contexts.Count;

            public Fixture(bool unresolvableParentRule = false)
            {
                var builder = new ContainerBuilder();
                builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
                builder.RegisterInstance(new NetworkSegment
                {
                    Logger = NullLogger<NetworkSegment>.Instance,
                    Device = null!, LocalRange = null!, Ranges = null!
                });
                builder.RegisterInstance(Detector).Named<IKnockDetector>("test");
                builder.RegisterType<KnockService>().SingleInstance();
                builder.RegisterType<NetworkHostContext>()
                    .WithParameter(TypedParameter.From(new NetworkMonitorConfig())).ExternallyOwned();
                builder.RegisterType<NetworkJanitor>()
                    .WithParameter(TypedParameter.From(new SweepOptions())).SingleInstance();
                builder.RegisterType<NetworkKnockContext>()
                    .WithParameter(TypedParameter.From(new NetworkMonitorConfig())).ExternallyOwned();
                if (unresolvableParentRule)
                    builder.Register<PacketFilterRule>(_ => throw new InvalidOperationException("Unrelated rule resolved"));
                _container = builder.Build();
                Service = _container.Resolve<KnockService>();

                // Exercise CreateKnockContext against a real Autofac graph without constructing
                // native capture and discovery services, which are unrelated to stanza ownership.
                Context = (NetworkContext)RuntimeHelpers.GetUninitializedObject(typeof(NetworkContext));
                typeof(NetworkContext).GetProperty("Config")!.SetValue(Context, new NetworkMonitorConfig());
                typeof(FilterContext).GetField("_dynamicHostFilters", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Context, new System.Collections.Concurrent.ConcurrentBag<HostFilterRuleInfo>());
                typeof(NetworkContext).GetField("_hostContexts", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Context, new List<NetworkHostContext>());
                typeof(NetworkContext).GetField("_knockContexts", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Context, _contexts);
                typeof(MadWizard.Desomnia.Network.Context.Context).GetProperty("Scope", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Context, _container);
            }

            public void Process()
            {
                var packet = new EthernetPacket(PhysicalAddress.Parse("001122334455"), PhysicalAddress.Parse("AABBCCDDEEFF"), EthernetType.IPv4)
                {
                    PayloadPacket = new IPv4Packet(Peer, IPAddress.Parse("192.0.2.20"))
                    {
                        PayloadPacket = new UdpPacket(50000, 62201)
                    }
                };
                ((INetworkService)Service).ProcessPacket(packet);
            }

            public void Dispose()
            {
                Context.Dispose();
            }
        }
    }
}
