using Autofac;
using MadWizard.Desomnia.Firewall.Manager;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MadWizard.Desomnia.Firewall.Tests
{
    public class ModuleTests
    {
        [Fact]
        public void NoBackendLeavesNetworkServicesUnchanged()
        {
            var builder = new ContainerBuilder();
            builder.RegisterModule<Module>();
            using var container = builder.Build();
            using var network = container.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag);

            Assert.False(network.IsRegistered<FirewallMonitor>());
            Assert.Empty(network.Resolve<IEnumerable<INetworkService>>());
        }

        [Fact]
        public void BackendEnablesOneMonitorPerNetwork()
        {
            using var monitor = new NetworkMonitor
            {
                Name = "test", Options = default, Device = null!, Network = null!, Janitor = null!,
                Logger = NullLogger<NetworkMonitor>.Instance
            };
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new TestFirewall()).As<IFirewallManager>();
            builder.RegisterInstance(monitor).ExternallyOwned();
            builder.RegisterInstance(NullLogger<FirewallMonitor>.Instance).As<ILogger<FirewallMonitor>>();
            // Application hosts load the core module after platform plugins supply their backend.
            builder.RegisterModule<Module>();
            using var container = builder.Build();
            using var first = container.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag);
            using var second = container.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkLifetimeScopeTag);

            var firewall = first.Resolve<FirewallMonitor>();
            Assert.Same(firewall, Assert.Single(first.Resolve<IEnumerable<INetworkService>>()));
            Assert.NotSame(firewall, second.Resolve<FirewallMonitor>());
        }

        private sealed class TestFirewall : IFirewallManager
        {
            public IDisposable Install(NetworkHost host, IReadOnlyCollection<FirewallRule> rules) =>
                throw new InvalidOperationException("Container activation must not install firewall rules.");
        }
    }
}
