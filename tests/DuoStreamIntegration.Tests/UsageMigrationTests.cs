using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Service.Duo.Configuration.Migration;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Service.Duo.Tests
{
    public class UsageMigrationTests
    {
        [Fact]
        public void OnlyAggregateDuoDemandIsRenamedToUsage()
        {
            var document = XDocument.Parse("""
                <SystemMonitor>
                  <DuoStreamMonitor onDemand="sleepless" onInstanceDemand="start">
                    <Instance name="Player" onDemand="start" />
                  </DuoStreamMonitor>
                </SystemMonitor>
                """);

            V2.Run(document);

            var monitor = document.Descendants("DuoSessionMonitor").Single();
            Assert.Equal("sleepless", monitor.Attribute("onUsage")?.Value);
            Assert.Null(monitor.Attribute("onDemand"));
            Assert.Equal("start", monitor.Attribute("onInstanceDemand")?.Value);

            var instance = document.Descendants("Instance").Single();
            Assert.Equal("start", instance.Attribute("onDemand")?.Value);
            Assert.Null(instance.Attribute("onUsage"));
        }

        [Fact]
        public void SessionInspectionDemandAttributesAreRenamedToUsage()
        {
            var document = XDocument.Parse("""
                <SystemMonitor>
                  <SessionMonitor onDemand="notify">
                    <User name="Player">
                      <Process name="game" onSessionDemand="notify">game</Process>
                    </User>
                  </SessionMonitor>
                </SystemMonitor>
                """);

            ((IXConfigurationMigration)new Session.Module()).Run(document, 2);

            var monitor = document.Descendants("SessionMonitor").Single();
            Assert.Equal("notify", monitor.Attribute("onUsage")?.Value);
            Assert.Null(monitor.Attribute("onDemand"));

            var process = document.Descendants("Process").Single();
            Assert.Equal("notify", process.Attribute("onSessionUsage")?.Value);
            Assert.Null(process.Attribute("onSessionDemand"));
        }
    }
}
