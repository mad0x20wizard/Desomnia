using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Configuration.Migration;
using Microsoft.Extensions.Configuration;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests;

public class NetworkInterfaceConfigurationTests
{
    [Fact]
    public void NamedSelectorsBindInDocumentOrderAndNestedDeclarationsAreIgnored()
    {
        var config = ReadConfiguration("""
            <SystemMonitor>
              <NetworkInterface name="z.*" disabled="true" allowToChange="disabled" monitor="false" />
              <NetworkInterface name="eth0" disabled="false" monitor="true" />
              <NetworkInterface name="a.*" />
              <NetworkMonitor interface="eth0">
                <NetworkInterface name="eth0" disabled="not-a-bool" />
                <NetworkInterfaceBlock interface="eth0" />
              </NetworkMonitor>
            </SystemMonitor>
            """);
        Assert.Equal(["z.*", "eth0", "a.*"], config.NetworkInterface.Select(n => n.Name));
        Assert.Equal(true, config.NetworkInterface[0].Disabled);
        Assert.Equal(NetworkInterfaceState.Disabled, config.NetworkInterface[0].AllowToChange);
        Assert.Equal(false, config.NetworkInterface[1].Disabled);
        Assert.Null(config.NetworkInterface[1].AllowToChange);
        Assert.Null(config.NetworkInterface[2].Disabled);
        Assert.Equal(false, config.NetworkInterface[0].Monitor);
        Assert.Equal(true, config.NetworkInterface[1].Monitor);
        Assert.Null(config.NetworkInterface[2].Monitor);
    }

    [Theory]
    [InlineData(null, "disabled")]
    [InlineData("false", "disabled")]
    [InlineData("true", null)]
    public void Version3MigratesRootBlocks(string? force, string? allowed)
    {
        var document = XDocument.Parse("""
            <Environment name="office"><SystemMonitor>
              <NetworkInterfaceBlock interface="eth0" />
              <NetworkMonitor><NetworkInterfaceBlock interface="wlan0" /></NetworkMonitor>
            </SystemMonitor></Environment>
            """);
        var block = document.Descendants("NetworkInterfaceBlock").First();
        if (force is not null) block.SetAttributeValue("force", force);
        V3.Run(document);
        var nic = Assert.Single(document.Descendants("NetworkInterface"));
        Assert.Equal("eth0", nic.Attribute("name")?.Value);
        Assert.Equal("true", nic.Attribute("disabled")?.Value);
        Assert.Equal(allowed, nic.Attribute("allowToChange")?.Value);
        Assert.Null(nic.Attribute("force"));
        Assert.Empty(document.Descendants("NetworkInterfaceBlock"));
        string migrated = document.ToString();
        V3.Run(document);
        Assert.Equal(migrated, document.ToString());
    }
    private static ModuleConfig<NetworkMonitorConfig> ReadConfiguration(string xml)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Add(new ExtendedXmlConfigurationSource(path)
            {
                Collections = CollectionElements.Derive([typeof(ModuleConfig<NetworkMonitorConfig>)])
            }).Build();
            return StrictConfigurationBinder.Get<ModuleConfig<NetworkMonitorConfig>>(configuration,
                options => options.BindNonPublicProperties = true)!;
        }
        finally { File.Delete(path); }
    }

}
