using MadWizard.Desomnia.Configuration.Migration;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class UsageMigrationTests
    {
        [Fact]
        public void RootSystemMonitorDemandIsRenamedToUsage()
        {
            var document = XDocument.Parse("""<SystemMonitor onDemand="sleepless" />""");

            V2.Run(document);

            Assert.Equal("sleepless", document.Root!.Attribute("onUsage")?.Value);
            Assert.Null(document.Root.Attribute("onDemand"));
        }

        [Fact]
        public void EnvironmentSystemMonitorDemandIsRenamedWithoutTouchingNestedModules()
        {
            var document = XDocument.Parse("""
                <EnvironmentMonitor>
                  <Environment>
                    <SystemMonitor onDemand="sleepless">
                      <NetworkMonitor onDemand="notify" />
                    </SystemMonitor>
                  </Environment>
                </EnvironmentMonitor>
                """);

            V2.Run(document);

            var system = document.Descendants("SystemMonitor").Single();
            Assert.Equal("sleepless", system.Attribute("onUsage")?.Value);
            Assert.Null(system.Attribute("onDemand"));

            var network = document.Descendants("NetworkMonitor").Single();
            Assert.Equal("notify", network.Attribute("onDemand")?.Value);
            Assert.Null(network.Attribute("onUsage"));
        }
    }
}
