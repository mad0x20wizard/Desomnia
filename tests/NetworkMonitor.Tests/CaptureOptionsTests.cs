using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class CaptureOptionsTests
{
    [Theory]
    [InlineData("", null, null)]
    [InlineData("bpf=\"auto\"", "auto", null)]
    [InlineData("bpf=\"false\"", "false", null)]
    [InlineData("bpfLimit=\"4000\"", null, 4000)]
    [InlineData("bpf=\"tcp port 80 or arp\" bpfLimit=\"100\"", "tcp port 80 or arp", 100)]
    [InlineData("bpf=\"true\"", "true", null)]
    public void BPFSettingsBindFromMonitorAttributes(string attributes, string? expression, int? limit)
    {
        var config = Assert.Single(Read($"<NetworkMonitor {attributes} />"));
        var options = Assert.IsType<BPFOptions>(config.MakeBPFOptions());
        Assert.Equal(expression, options.Expression);
        Assert.Equal(limit, options.InstructionLimit);
    }

    [Theory]
    [InlineData("bpf=\"\"")]
    [InlineData("bpf=\"   \"")]
    [InlineData("bpf=\"\" bpfLimit=\"4000\"")]
    public void DisabledBPFReturnsNoOptions(string attributes)
    {
        var config = Assert.Single(Read($"<NetworkMonitor {attributes} />"));
        Assert.Null(config.MakeBPFOptions());
    }

    [Theory]
    [InlineData("", 1, 30)]
    [InlineData("reassemble=\"none\" reassembleTimeout=\"23s\"", 0, 23)]
    [InlineData("reassemble=\"IPv4|IPv6\" reassembleTimeout=\"2min\"", 3, 120)]
    [InlineData("reassemble=\"IPv4|IPv6|Passthrough\"", 7, 30)]
    [InlineData("reassemble=\"IPv6|Passthrough\"", 6, 30)]
    public void ReassemblySettingsBindFromMonitorAttributes(string attributes, int family, int seconds)
    {
        var config = Assert.Single(Read($"<NetworkMonitor {attributes} />"));
        var options = config.MakeReassemblyOptions();
        Assert.Equal((ReassemblyProtocol)family, options.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(seconds), options.Timeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveInstructionBudgetIsRejected(int limit)
    {
        var config = Assert.Single(Read($"<NetworkMonitor bpfLimit=\"{limit}\" />"));
        Assert.Throws<ArgumentOutOfRangeException>(nameof(BPFOptions.InstructionLimit), () => config.MakeBPFOptions());
    }

    [Theory]
    [InlineData("reassemble=\"8\"", nameof(ReassemblyOptions.Protocol))]
    [InlineData("reassembleTimeout=\"0s\"", nameof(ReassemblyOptions.Timeout))]
    [InlineData("reassembleTimeout=\"-00:00:01\"", nameof(ReassemblyOptions.Timeout))]
    public void InvalidReassemblyOptionsAreRejected(string attributes, string property)
    {
        var config = Assert.Single(Read($"<NetworkMonitor {attributes} />"));
        Assert.Throws<ArgumentOutOfRangeException>(property, () => config.MakeReassemblyOptions());
    }

    [Fact]
    public void NetworkMonitorsKeepSeparateCaptureOptions()
    {
        var configs = Read("""
            <NetworkMonitor bpf="arp" bpfLimit="40" reassemble="none" reassembleTimeout="5s" />
            <NetworkMonitor bpf="" reassemble="IPv6|Passthrough" />
            <NetworkMonitor />
            """);
        var first = Assert.IsType<BPFOptions>(configs[0].MakeBPFOptions());
        Assert.Equal("arp", first.Expression);
        Assert.Equal(40, first.InstructionLimit);
        Assert.Equal(TimeSpan.FromSeconds(5), configs[0].MakeReassemblyOptions().Timeout);
        Assert.Null(configs[1].MakeBPFOptions());
        Assert.Equal((ReassemblyProtocol)6, configs[1].MakeReassemblyOptions().Protocol);
        var defaults = Assert.IsType<BPFOptions>(configs[2].MakeBPFOptions());
        Assert.Null(defaults.Expression);
        Assert.Null(defaults.InstructionLimit);
        Assert.Equal(ReassemblyProtocol.IPv4, configs[2].MakeReassemblyOptions().Protocol);
    }

    private static IList<NetworkMonitorConfig> Read(string monitors)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"<SystemMonitor>{monitors}</SystemMonitor>");
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Add(new ExtendedXmlConfigurationSource(path)
            {
                Collections = CollectionElements.Derive([typeof(ModuleConfig<NetworkMonitorConfig>)])
            }).Build();
            return StrictConfigurationBinder.Get<ModuleConfig<NetworkMonitorConfig>>(configuration,
                options => options.BindNonPublicProperties = true)!.NetworkMonitor;
        }
        finally { File.Delete(path); }
    }
}
