using MadWizard.Desomnia.Display.Configuration.Migration;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Display.Tests
{
    public class UsageMigrationTests
    {
        [Fact]
        public void DisplayMonitorDemandIsRenamedToUsage()
        {
            var document = XDocument.Parse("""
                <SystemMonitor>
                  <DisplayMonitor onDemand="notify" />
                </SystemMonitor>
                """);

            V2.Run(document);

            var monitor = document.Descendants("DisplayMonitor").Single();
            Assert.Equal("notify", monitor.Attribute("onUsage")?.Value);
            Assert.Null(monitor.Attribute("onDemand"));
        }
    }
}
