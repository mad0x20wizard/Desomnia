using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The migration layer as a file-provider decorator: transparent serving of the migrated
    /// form to everything reading through the source's provider, pristine passthrough without
    /// it, and the throw-through policy for content that cannot be served.
    /// </summary>
    public class MigratingFileProviderTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;
        private readonly string _path;

        public MigratingFileProviderTests() => _path = Path.Combine(_directory, "monitor.xml");

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private sealed class RenamingModule : ConfigurableModule, IXConfigurationMigration
        {
            protected internal override uint MinVersion => 2;

            protected internal override uint MaxVersion => uint.MaxValue;

            void IXConfigurationMigration.Run(XDocument configuration, uint version)
            {
                foreach (var attribute in configuration.Descendants("NetworkMonitor").Attributes("watchUDPPort").ToList())
                    attribute.MigrateRename("watchPort");
            }
        }

        private (ExtendedXmlConfigurationSource Source, XConfigurationMigrator Migrator) CreateDecoratedSource()
        {
            var registry = new VersionedModuleRegistry { LatestVersion = 2 };
            registry.Register(new RenamingModule());

            var source = new ExtendedXmlConfigurationSource(_path);

            var migrator = new XConfigurationMigrator(registry, () => source.FullPath) { Logger = NullLogger.Instance };

            source.FileProvider = new MigratingFileProvider(source.FileProvider!, migrator, source.Path!);

            return (source, migrator);
        }

        [Fact]
        public void Transient_ServesTheMigratedForm_WithoutTouchingTheFile()
        {
            File.WriteAllText(_path, """<SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" /></SystemMonitor>""");

            var (source, _) = CreateDecoratedSource();

            var content = new ConfigurationBuilder().Add(source).Build();

            // the provider reads the migrated form (no header needed: transient is the default) ...
            Assert.Equal("9", content["NetworkMonitor:watchPort"]);
            Assert.Null(content["NetworkMonitor:watchUDPPort"]);
            Assert.Null(content["version"]); // the stamped declaration is not data

            // ... while the file on disk stays exactly as the user wrote it
            Assert.Equal("""<SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" /></SystemMonitor>""", File.ReadAllText(_path));
        }

        [Fact]
        public void WithoutTheDecorator_TheSourceServesTheFileAsIs()
        {
            // the composition is removable: a pristine source has no version or migration knowledge
            File.WriteAllText(_path, """<SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" /></SystemMonitor>""");

            var content = new ConfigurationBuilder().Add(new ExtendedXmlConfigurationSource(_path)).Build();

            Assert.Equal("9", content["NetworkMonitor:watchUDPPort"]);
            Assert.Null(content["NetworkMonitor:watchPort"]);
        }

        [Fact]
        public void BadContent_Throws_AlsoAfterASuccessfulRead()
        {
            // a configuration that cannot be served stops the application, by design - the
            // decorator never swallows a failure into stale content. (In production the reload
            // read happens on the stock provider machinery's watcher callback, where the
            // exception terminates the process; the decorator logs and flushes before it
            // throws, so the log tells why. Not exercised with a live watcher here - the
            // unhandled exception would take the test host with it.)
            File.WriteAllText(_path, """<SystemMonitor><NetworkMonitor name="Ethernet" watchUDPPort="9" /></SystemMonitor>""");

            var (source, _) = CreateDecoratedSource();

            var content = new ConfigurationBuilder().Add(source).Build();

            Assert.Equal("9", content["NetworkMonitor:watchPort"]);

            File.WriteAllText(_path, "<SystemMonitor><NetworkMonitor"); // a half-written edit

            var file = source.FileProvider!.GetFileInfo(source.Path!);

            Assert.ThrowsAny<Exception>(() => file.CreateReadStream());
        }
    }
}
