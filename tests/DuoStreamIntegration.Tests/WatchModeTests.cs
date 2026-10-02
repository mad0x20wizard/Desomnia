using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DuoStreamIntegration.Tests;

public sealed class WatchModeTests
{
    [Theory]
    [InlineData("auto", WatchMode.Auto)]
    [InlineData("registry", WatchMode.Registry)]
    [InlineData("event-log | polling", WatchMode.EventLog | WatchMode.Polling)]
    [InlineData("Registry, Capture", WatchMode.Registry | WatchMode.Capture)]
    [InlineData("polling | listener", WatchMode.Polling | WatchMode.Listener)]
    public void XML_binds_single_modes_and_flag_combinations(string value, WatchMode expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"""
                <SystemMonitor version="2">
                  <SessionMonitor />
                  <DuoSessionMonitor watchMode="{value}" />
                </SystemMonitor>
                """);
            var source = new ExtendedXmlConfigurationSource(path)
            {
                Collections = CollectionElements.Derive([typeof(DuoConfig)])
            };
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Add(source).Build();
            var config = StrictConfigurationBinder.Get<DuoConfig>(configuration,
                options => options.BindNonPublicProperties = true)!;

            Assert.Equal(expected, config.DuoSessionMonitor!.WatchMode);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(WatchMode.Auto, false, true)]
    [InlineData(WatchMode.Auto, true, false)]
    [InlineData(WatchMode.Registry, false, true)]
    [InlineData(WatchMode.Polling, true, false)]
    [InlineData(WatchMode.Listener, false, true)]
    [InlineData(WatchMode.Registry | WatchMode.Listener, true, true)]
    [InlineData(WatchMode.Capture, true, false)]
    [InlineData(WatchMode.EventLog | WatchMode.Capture, true, false)]
    public void Traffic_mode_uses_the_explicit_flag_or_network_configuration(WatchMode mode, bool network, bool listener)
    {
        var config = Config(mode, network);

        Assert.Equal(listener, config.UseListener);
    }

    [Theory]
    [InlineData(WatchMode.Listener | WatchMode.Capture, false)]
    [InlineData(WatchMode.Listener | WatchMode.Capture, true)]
    [InlineData(WatchMode.Capture, false)]
    [InlineData(WatchMode.Polling | WatchMode.Capture, false)]
    public void Invalid_traffic_modes_are_rejected(WatchMode mode, bool network)
    {
        var config = Config(mode, network);

        Assert.Throws<FormatException>(() => config.UseListener);
    }

    [Theory]
    [InlineData(WatchMode.Listener)]
    [InlineData(WatchMode.Capture)]
    public void Selecting_only_a_traffic_mode_preserves_automatic_session_watcher_selection(WatchMode mode)
    {
        var automatic = new DuoSessionMonitorConfig { ServiceName = "TestDuo" };
        var traffic = new DuoSessionMonitorConfig { ServiceName = "TestDuo", WatchMode = mode };

        Assert.Equal(new[] { WatchMode.Registry, WatchMode.EventLog, WatchMode.Polling }, automatic.WatchModes());
        Assert.Equal(automatic.WatchModes(), traffic.WatchModes());
    }

    private static DuoConfig Config(WatchMode mode, bool network)
    {
        var config = new DuoConfig
        {
            DuoSessionMonitor = new DuoSessionMonitorConfig { ServiceName = "TestDuo", WatchMode = mode }
        };
        if (network) config.NetworkMonitor.Add(new NetworkMonitorConfig());
        return config;
    }
}
