using Autofac;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using System.Net;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class NetworkJanitorTests
    {
        private static readonly IPAddress Peer = IPAddress.Parse("192.0.2.10");

        [Fact]
        public async Task DeadlinesIncludeObjectGraceAndCleanupRunsAtTheBoundary()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            var host = fixture.AddHost();
            var service = new TestService();
            var expires = DateTime.Now.AddHours(1);
            host.AddAddress(Peer, new() { Expires = expires });
            host.AddService(service, new() { Expires = expires });
            range.AddAddress(Peer, new() { Expires = expires });
            fixture.Janitor.WatchRange(range);

            Assert.Equal(expires.AddSeconds(5), fixture.Sweep(expires));
            Assert.True(range.Contains(Peer));
            Assert.Equal(expires.AddMinutes(5), fixture.Sweep(expires.AddSeconds(5)));
            Assert.Empty(range);
            Assert.Contains(Peer, host.IPAddresses);
            Assert.Contains(service, host.Services);
            Assert.Null(fixture.Sweep(expires.AddMinutes(5)));
            Assert.Empty(host.IPAddresses);
            Assert.Empty(host.Services);
        }

        [Fact]
        public async Task RenewalReplacesTheOldDeadlineForAllEntries()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            var host = fixture.AddHost();
            var service = new TestService();
            var expires = DateTime.Now.AddHours(1);
            host.AddAddress(Peer, new() { Expires = expires });
            host.AddService(service, new() { Expires = expires });
            range.AddAddress(Peer, new() { Expires = expires });
            fixture.Janitor.WatchRange(range);

            var renewed = expires.AddHours(1);
            host.AddAddress(Peer, new() { Expires = renewed });
            host.AddService(service, new() { Expires = renewed });
            range.AddAddress(Peer, new() { Expires = renewed });

            Assert.Equal(renewed.AddSeconds(5), fixture.Sweep(expires.AddMinutes(10)));
            Assert.Contains(Peer, host.IPAddresses);
            Assert.Contains(service, host.Services);
            Assert.True(range.Contains(Peer));
        }

        [Fact]
        public async Task IdleWorkerWakesForAnEarlierDeadlineAndUsesTheNetworkMutex()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            range.AddAddress(Peer, new(TimeSpan.FromDays(365)));
            fixture.Janitor.WatchRange(range);
            fixture.Janitor.StartSweeping();
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            range.AddressRangeRemoved += (_, _) => removed.TrySetResult();

            using (await fixture.Network.Mutex.LockAsync())
            {
                range.RemoveAddress(Peer);
                removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                range.AddAddress(Peer, new() { Expires = DateTime.Now.AddSeconds(-6) });
                await Task.Delay(100);
                Assert.True(range.Contains(Peer));
                Assert.False(removed.Task.IsCompleted);
            }

            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(range);
        }

        [Fact]
        public async Task TimerWakesAtExpirationPlusGraceWithoutAnotherChange()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            var deadline = DateTime.Now.AddMilliseconds(250);
            range.AddAddress(Peer, new() { Expires = deadline - TimeSpan.FromSeconds(5) });
            fixture.Janitor.WatchRange(range);
            var removed = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
            range.AddressRangeRemoved += (_, _) => removed.TrySetResult(DateTime.Now);
            fixture.Janitor.StartSweeping();

            Assert.True(await removed.Task.WaitAsync(TimeSpan.FromSeconds(5)) >= deadline);
            Assert.Empty(range);
        }

        [Fact]
        public async Task PendingCleanupRechecksRenewalAfterAcquiringTheMutex()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            var originalDeadline = DateTime.Now.AddMilliseconds(250);
            range.AddAddress(Peer, new() { Expires = originalDeadline - TimeSpan.FromSeconds(5) });
            fixture.Janitor.WatchRange(range);
            fixture.Janitor.StartSweeping();
            var removed = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
            range.AddressRangeRemoved += (_, _) => removed.TrySetResult(DateTime.Now);
            DateTime renewedDeadline;
            using (await fixture.Network.Mutex.LockAsync())
            {
                await Task.Delay(350); // let the old timer fire while cleanup cannot acquire the mutex
                renewedDeadline = DateTime.Now.AddMilliseconds(250);
                range.AddAddress(Peer, new() { Expires = renewedDeadline - TimeSpan.FromSeconds(5) });
                Assert.False(removed.Task.IsCompleted);
            }

            Assert.True(await removed.Task.WaitAsync(TimeSpan.FromSeconds(5)) >= renewedDeadline);
            Assert.Empty(range);
        }

        [Fact]
        public async Task HostsAddedWhileIdleAreWatchedForExpiration()
        {
            await using var fixture = new Fixture();
            fixture.Janitor.StartSweeping();
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            NetworkHost host;
            using (await fixture.Network.Mutex.LockAsync())
            {
                host = fixture.AddHost();
                host.AddressRemoved += (_, _) => removed.TrySetResult();
                host.AddAddress(Peer, new() { Expires = DateTime.Now.AddMinutes(-6) });
            }
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(host.IPAddresses);
        }

        [Fact]
        public async Task StopPreventsCleanupAndRestartFindsChangesMadeWhileStopped()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            fixture.Janitor.WatchRange(range);
            fixture.Janitor.StartSweeping();
            await fixture.Janitor.StopSweeping();
            range.AddAddress(Peer, new() { Expires = DateTime.Now.AddSeconds(-6) });
            var host = fixture.AddHost();
            host.AddAddress(Peer, new() { Expires = DateTime.Now.AddMinutes(-6) });
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.AddressRemoved += (_, _) => removed.TrySetResult();
            Assert.True(range.Contains(Peer));
            Assert.Contains(Peer, host.IPAddresses);
            fixture.Janitor.StartSweeping();
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Janitor.StopSweeping();
            Assert.Empty(range);
            Assert.Empty(host.IPAddresses);
        }

        [Fact]
        public async Task ReleasingTheLastFilterReferenceWakesAnOtherwiseIdleJanitor()
        {
            await using var fixture = new Fixture();
            using var context = fixture.CreateContext();
            using var first = new DynamicHostFilterRule { Host = context.Host, Type = FilterRuleType.Must };
            using var second = new DynamicHostFilterRule { Host = context.Host, Type = FilterRuleType.Must };
            fixture.Janitor.MakeHostEligibleForSweeping(context);

            ((IDisposable)first).Dispose();
            Assert.Null(fixture.Sweep(DateTime.Now));
            Assert.Same(context.Host, Assert.Single(fixture.Network));
            Assert.True(context.Host.IsReferencedByFilterRules);
            fixture.Janitor.StartSweeping();
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Network.HostRemoved += (_, _) => removed.TrySetResult();
            using (await fixture.Network.Mutex.LockAsync())
                ((IDisposable)second).Dispose();

            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(fixture.Network);
        }

        [Fact]
        public async Task ServiceAndRouterLifetimesDelayContextDisposal()
        {
            await using var fixture = new Fixture();
            var router = new NetworkRouter("router")
            {
                Network = fixture.Network, Options = new(), VPNClients = [],
                ValidUntil = DateTime.Now.AddHours(1)
            };
            using var context = fixture.CreateContext(router);
            fixture.Janitor.MakeHostEligibleForSweeping(context);
            var service = new TestService();
            var expires = router.ValidUntil.Value.AddHours(1);
            router.AddService(service, new() { Expires = expires });
            Assert.Equal(expires.AddMinutes(5), fixture.Sweep(router.ValidUntil.Value));
            Assert.Same(router, fixture.Network[router.Name]);
            router.RemoveService(service);
            var renewed = expires.AddHours(1);
            router.ValidUntil = renewed;
            Assert.Equal(renewed, fixture.Sweep(expires));
            fixture.Sweep(renewed);
            Assert.Empty(fixture.Network);
        }

        [Fact]
        public async Task DisposedRangesAndContextsAreNoLongerSwept()
        {
            await using var fixture = new Fixture();
            var range = new NetworkHostRange();
            fixture.Janitor.WatchRange(range);
            range.AddAddress(Peer, new() { Expires = DateTime.Now.AddSeconds(-6) });
            ((IDisposable)range).Dispose();
            using var context = fixture.CreateContext();
            fixture.Janitor.MakeHostEligibleForSweeping(context);
            context.Dispose();

            Assert.Null(fixture.Sweep(DateTime.Now));
            Assert.True(range.Contains(Peer));
            Assert.Empty(fixture.Network);
        }

        [Fact]
        public async Task PermanentEntriesNeedNoTimerAndExtremeDeadlinesDoNotOverflow()
        {
            await using var fixture = new Fixture();
            using var range = new NetworkHostRange();
            fixture.Janitor.WatchRange(range);
            range.AddAddress(Peer);
            Assert.Null(fixture.Sweep(DateTime.Now));
            range.RemoveAddress(Peer);
            range.AddAddress(Peer, new() { Expires = DateTime.MaxValue });
            Assert.Equal(DateTime.MaxValue, fixture.Sweep(DateTime.Now));
        }

        [Fact]
        public async Task HostChangesShareOneNotificationIncludingRenewalsAndRouterValidity()
        {
            await using var fixture = new Fixture();
            var host = new NetworkRouter("router") { Network = fixture.Network, Options = new(), VPNClients = [] };
            int changes = 0;
            host.Changed += (_, _) => changes++;
            host.AddAddress(Peer, new(TimeSpan.FromMinutes(1)));
            host.AddAddress(Peer, new(TimeSpan.FromMinutes(2)));
            var service = new TestService();
            host.AddService(service, new(TimeSpan.FromMinutes(1)));
            host.AddService(service, new(TimeSpan.FromMinutes(2)));
            using var rule = new DynamicHostFilterRule { Host = host, Type = FilterRuleType.Must };
            ((IDisposable)rule).Dispose();
            ((IDisposable)rule).Dispose(); // repeated disposal must not notify again
            host.ValidUntil = DateTime.Now.AddHours(1);
            host.RemoveAddress(Peer);
            host.RemoveService(service);
            Assert.Equal(9, changes);
        }

        private sealed class TestService() : NetworkService("test")
        {
            public override bool Accepts(Packet packet, FlowDirection direction) => false;
        }

        private sealed class TestContext : NetworkHostContext
        {
            public TestContext(ILifetimeScope parent, NetworkHost host) : base(parent, AutoDiscoveryType.Nothing)
            {
                Scope = parent.BeginLifetimeScope(builder => builder.RegisterInstance(host).As<NetworkHost>());
            }
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly IContainer _container;
            public NetworkSegment Network { get; } = new()
            {
                Logger = NullLogger<NetworkSegment>.Instance,
                Device = null!, LocalRange = null!, Ranges = null!
            };
            public NetworkJanitor Janitor { get; }
            public Fixture()
            {
                var builder = new ContainerBuilder();
                builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
                _container = builder.Build();
                Janitor = new() { Network = Network, Logger = NullLogger<NetworkJanitor>.Instance };
            }
            public NetworkHost AddHost()
            {
                var host = new NetworkHost("host") { Network = Network };
                Network.AddHost(host);
                return host;
            }
            public NetworkHostContext CreateContext(NetworkHost? host = null)
            {
                host ??= new NetworkHost("host") { Network = Network };
                var context = new TestContext(_container, host);
                Network.AddHost(host);
                context.Scope.CurrentScopeEnding += (_, _) => Network.RemoveHost(host);
                return context;
            }
            public DateTime? Sweep(DateTime now)
            {
                using (Network.Mutex.Lock())
                    return Janitor.Sweep(now);
            }
            public async ValueTask DisposeAsync()
            {
                await Janitor.StopSweeping();
                Janitor.Dispose();
                _container.Dispose();
            }
        }
    }
}
