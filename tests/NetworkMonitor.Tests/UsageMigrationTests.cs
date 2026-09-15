using MadWizard.Desomnia.Network.Configuration.Migration;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class UsageMigrationTests
    {
        [Fact]
        public void OnlyAggregateNetworkDemandIsRenamedToUsage()
        {
            var document = XDocument.Parse("""
                <SystemMonitor>
                  <NetworkMonitor onDemand="notify">
                    <RemoteHost name="server" onDemand="wake">
                      <Service name="SSH" protocol="TCP" port="22" onDemand="knock" />
                    </RemoteHost>
                  </NetworkMonitor>
                </SystemMonitor>
                """);

            V2.Run(document);

            var monitor = document.Descendants("NetworkMonitor").Single();
            Assert.Equal("notify", monitor.Attribute("onUsage")?.Value);
            Assert.Null(monitor.Attribute("onDemand"));

            var host = document.Descendants("RemoteHost").Single();
            Assert.Equal("wake", host.Attribute("onDemand")?.Value);
            Assert.Null(host.Attribute("onUsage"));

            var service = document.Descendants("Service").Single();
            Assert.Equal("knock", service.Attribute("onDemand")?.Value);
            Assert.Null(service.Attribute("onUsage"));
        }
    }
}
