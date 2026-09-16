using MadWizard.Desomnia.Processes.Configuration.Migration;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    public class UsageMigrationTests
    {
        [Fact]
        public void ProcessDemandAttributesAreRenamedToUsage()
        {
            var document = XDocument.Parse("""
                <SystemMonitor>
                  <ProcessMonitor onDemand="notify">
                    <Process name="game" onDemand="notify">game</Process>
                  </ProcessMonitor>
                  <VirtualHost name="vm" onDemand="start" />
                </SystemMonitor>
                """);

            V2.Run(document);

            var monitor = document.Descendants("ProcessMonitor").Single();
            Assert.Equal("notify", monitor.Attribute("onUsage")?.Value);
            Assert.Null(monitor.Attribute("onDemand"));

            var process = document.Descendants("Process").Single();
            Assert.Equal("notify", process.Attribute("onUsage")?.Value);
            Assert.Null(process.Attribute("onDemand"));

            var host = document.Descendants("VirtualHost").Single();
            Assert.Equal("start", host.Attribute("onDemand")?.Value);
            Assert.Null(host.Attribute("onUsage"));
        }
    }
}
