using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    /// <summary>
    /// The ordinal is the identity that correlates the module's and the plugins' views of the
    /// same configuration: every view binds its own list from the same sections in document
    /// order, so the position — stamped by <see cref="ModuleConfig{T}"/>'s list — is the same
    /// in each, whether or not a network carries a name.
    /// </summary>
    public class NetworkOrdinalTests
    {
        private sealed class PluginNetworkConfig : NetworkMonitorConfig
        {
            public string? Extra { get; set; }
        }

        [Fact]
        public void TheList_StampsTheOrdinal_InInsertionOrder()
        {
            var config = new ModuleConfig<NetworkMonitorConfig>();

            NetworkMonitorConfig first = new(), second = new();

            config.NetworkMonitor.Add(first);
            config.NetworkMonitor.Add(second);

            Assert.Equal(0, first.Ordinal);
            Assert.Equal(1, second.Ordinal);
        }

        [Fact]
        public void ModuleAndPluginViews_AgreeOnTheOrdinal_WithoutNames()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NetworkMonitor:0:interface"] = "en0",
                ["NetworkMonitor:1:name"] = "prod",
                ["NetworkMonitor:1:interface"] = "en1",
            }).Build();

            var module = StrictConfigurationBinder.Get<ModuleConfig<NetworkMonitorConfig>>(configuration,
                options => options.BindNonPublicProperties = true)!;
            var plugin = StrictConfigurationBinder.Get<ModuleConfig<PluginNetworkConfig>>(configuration,
                options => options.BindNonPublicProperties = true)!;

            Assert.Equal(["en0", "en1"], module.NetworkMonitor.Select(network => network.Interface));

            // the nameless network correlates by position alone
            Assert.Null(module.NetworkMonitor[0].Name);
            Assert.Equal(module.NetworkMonitor[0].Ordinal, plugin.NetworkMonitor[0].Ordinal);
            Assert.Equal(module.NetworkMonitor[1].Ordinal, plugin.NetworkMonitor[1].Ordinal);
            Assert.Equal("prod", plugin.NetworkMonitor[1].Name);
        }
    }
}
