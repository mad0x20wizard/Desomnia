using Autofac;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Sunshine.Watch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;
using Xunit;

namespace DuoStreamIntegration.Tests;

public sealed class InstanceFilterTests
{
    [Fact]
    public void XML_instance_filters_combine_inclusions_and_exclusions()
    {
        _ = new NetworkMonitorConfig();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                <SystemMonitor version="2">
                  <NetworkMonitor />
                  <DuoSessionMonitor>
                    <Instance name="Player">
                      <HostRangeFilterRule network="192.0.2.0/24" type="Must" />
                      <HostFilterRule IPv4="192.0.2.10" type="MustNot" />
                      <HostFilterRule IPv4="192.0.2.11" type="MustNot" />
                    </Instance>
                  </DuoSessionMonitor>
                </SystemMonitor>
                """);
            var source = new ExtendedXmlConfigurationSource(path)
            {
                Collections = CollectionElements.Derive([typeof(DuoConfig)])
            };
            var configuration = new ConfigurationBuilder().Add(source).Build();
            using var cleanup = (IDisposable)configuration;
            var config = StrictConfigurationBinder.Get<DuoConfig>(configuration,
                options => options.BindNonPublicProperties = true)!;
            var info = Assert.Single(config.DuoSessionMonitor!.Instance);
            Assert.Equal(2, info.HostFilterRule.Count);
            Assert.Single(info.HostRangeFilterRule);
            using var instance = new DuoInstance(info.Name!, DuoTestSupport.Settings(), info);
            using var container = Container();
            using var context = new TestContext(container, instance);

            Assert.False(context.Filter.ShouldFilter(Packet("192.0.2.12", 47989, false)));
            Assert.True(context.Filter.ShouldFilter(Packet("192.0.2.10", 47989, false)));
            Assert.True(context.Filter.ShouldFilter(Packet("192.0.2.11", 47989, false)));
            Assert.True(context.Filter.ShouldFilter(Packet("198.51.100.10", 47989, false)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false, false, "Must")]
    [InlineData(false, true, "Must")]
    [InlineData(true, false, "Must")]
    [InlineData(true, true, "Must")]
    [InlineData(false, false, "MustNot")]
    [InlineData(false, true, "MustNot")]
    [InlineData(true, false, "MustNot")]
    [InlineData(true, true, "MustNot")]
    public void Instance_filters_restrict_both_TCP_and_UDP_service_packets(bool range, bool udp, string type)
    {
        var info = BindFilters(range, type);
        using var instance = new DuoInstance("Player", DuoTestSupport.Settings(), info);
        using var container = Container();
        using var context = new TestContext(container, instance);
        var filter = context.Filter;
        var port = (ushort)(instance.Settings.Port + (udp ? 9 : 0));

        Assert.Equal(type == "MustNot", filter.ShouldFilter(Packet("192.0.2.10", port, udp)));
        Assert.Equal(type == "Must", filter.ShouldFilter(Packet("198.51.100.10", port, udp)));
        // A matching client must not make unrelated ports part of this service.
        Assert.True(filter.ShouldFilter(Packet("192.0.2.10", 12345, udp)));
    }

    [Fact]
    public void Filter_rules_are_scoped_to_their_instance()
    {
        using var container = Container();
        using var restricted = new DuoInstance("Restricted", DuoTestSupport.Settings(), BindFilters(false, "MustNot"));
        using var unrestricted = DuoTestSupport.Instance("Unrestricted");
        using var first = new TestContext(container, restricted);
        using var second = new TestContext(container, unrestricted);
        var packet = Packet("192.0.2.10", 47989, false);

        Assert.True(first.Filter.ShouldFilter(packet));
        Assert.False(second.Filter.ShouldFilter(packet));
        Assert.True(second.Filter.ShouldFilter(Packet("192.0.2.10", 12345, false)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Listener_mode_rejects_instance_filters(bool range, bool explicitListener)
    {
        var config = new DuoConfig
        {
            SessionMonitor = new MadWizard.Desomnia.Session.Configuration.SessionMonitorConfig(),
            DuoSessionMonitor = new DuoSessionMonitorConfig
            {
                ServiceName = "TestDuo", UsePolling = true, UseListener = explicitListener
            }
        };
        config.DuoSessionMonitor.Instance.Add(BindFilters(range, "Must"));
        if (explicitListener)
            config.NetworkMonitor.Add(new NetworkMonitorConfig());

        var error = Assert.Throws<FormatException>(() => new TestModule().Configure(new ContainerBuilder(), config));
        Assert.Contains("Player", error.Message);
        Assert.Contains("packet capture", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DuoInstanceWatchInfo BindFilters(bool range, string type)
    {
        _ = new NetworkMonitorConfig(); // Registers the network address converters used by XML binding.
        var rule = range ? "HostRangeFilterRule" : "HostFilterRule";
        var values = new Dictionary<string, string?>
        {
            ["Name"] = "Player",
            [$"{rule}:0:type"] = type,
            [$"{rule}:0:{(range ? "network" : "IPv4")}"] = range ? "192.0.2.0/24" : "192.0.2.10"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var info = StrictConfigurationBinder.Get<DuoInstanceWatchInfo>(configuration,
            options => options.BindNonPublicProperties = true)!;
        if (range)
            Assert.NotNull(Assert.Single(info.HostRangeFilterRule).AddressRange);
        else
            Assert.Equal(IPAddress.Parse("192.0.2.10"), Assert.Single(info.HostFilterRule).IPv4);
        return info;
    }

    private static IContainer Container()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterType<StaticHostFilterRule>();
        builder.RegisterType<StaticHostRangeFilterRule>();
        // Exercise the production packet filter without starting capture or OS services.
        builder.RegisterType(typeof(IPacketFilter).Assembly.GetType(
            "MadWizard.Desomnia.Network.Filter.PacketRuleFilter", throwOnError: true)!).As<IPacketFilter>()
            .InstancePerMatchingLifetimeScope(MatchingScopeLifetimeTags.NetworkServiceLifetimeScopeTag);
        return builder.Build();
    }

    private static EthernetPacket Packet(string source, ushort port, bool udp)
    {
        TransportPacket transport = udp ? new UdpPacket(50000, port) : new TcpPacket(50000, port);
        transport.PayloadData = [1, 2, 3];
        return new EthernetPacket(PhysicalAddress.Parse("001122334455"),
            PhysicalAddress.Parse("AABBCCDDEEFF"), EthernetType.IPv4)
        {
            PayloadPacket = new IPv4Packet(IPAddress.Parse(source), IPAddress.Parse("192.0.2.20"))
            {
                PayloadPacket = transport
            }
        };
    }

    private sealed class TestContext(ILifetimeScope parent, DuoInstance instance) : SunshineServiceContext(parent, instance)
    {
        public IPacketFilter Filter => Scope.Resolve<IPacketFilter>();
    }

    private sealed class TestModule : MadWizard.Desomnia.Service.Duo.PluginModule
    {
        public void Configure(ContainerBuilder builder, DuoConfig config) => base.Load(builder, config);
    }
}
