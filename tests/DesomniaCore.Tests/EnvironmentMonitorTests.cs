using Autofac;
using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Environments.Export;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class EnvironmentMonitorTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;

        private string WriteConfig(string content)
        {
            var path = Path.Combine(_directory, "monitor.xml");

            File.WriteAllText(path, content);

            return path;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        // the canonical harness: the physical source, the monitor (the application container
        // fills the required logger via the LoggingModule; here we do) and the pipeline stage
        // between them, with the persistent container standing in via Conditions()
        private static (ConfigurationPipeline Pipeline, EnvironmentMonitor Monitor) CreatePipeline(string path, FakeCondition? toggle = null)
        {
            var source = new ExtendedXmlConfigurationSource(path);

            var monitor = new EnvironmentMonitor { Logger = NullLogger.Instance };

            var pipeline = new ConfigurationPipeline(source, monitor, Conditions(toggle), new VersionedModuleRegistry { })
            {
                Logger = NullLogger.Instance,
            };

            return (pipeline, monitor);
        }

        private static IConfiguration BuildConfiguration(ConfigurationPipeline pipeline)
            => new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();

        [Fact]
        public void Start_LegacySystemMonitorRoot_IsPassthrough()
        {
            var path = WriteConfig("""<SystemMonitor />""");

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.False(pipeline.Augmenting);
        }

        [Fact]
        public void Start_LegacyDialectWithBareAttributes_IsFatal()
        {
            // value-less attributes make the file not well-formed; the legacy dialect is
            // gone, so this now fails at boot instead of falling back to a legacy provider
            var path = WriteConfig("""<SystemMonitor><NetworkMonitor><traffic must /></NetworkMonitor></SystemMonitor>""");

            var (pipeline, _) = CreatePipeline(path);

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.Start);

            Assert.Contains("well-formed", ex.Message);
        }

        [Fact]
        public void Start_MissingFile_IsPassthrough()
        {
            var (pipeline, _) = CreatePipeline(Path.Combine(_directory, "missing.xml"));

            pipeline.Start(); // the non-optional provider reports the missing file, not the pipeline

            Assert.False(pipeline.Augmenting);
        }

        [Fact]
        public void Start_UnknownRoot_Throws()
        {
            var path = WriteConfig("""<WrongRoot version="1" />""");

            var (pipeline, _) = CreatePipeline(path);

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.Start);

            Assert.Contains("SystemMonitor", ex.Message);
            Assert.Contains("EnvironmentMonitor", ex.Message);
        }

        [Fact]
        public void Start_BareAttributeBelowEnvironmentRoot_IsFatal()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <DefaultEnvironment><NetworkMonitor><traffic must /></NetworkMonitor></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.Start);

            Assert.Contains("well-formed", ex.Message);
        }

        [Fact]
        public void StartAndBind_MergesActiveBlocks_AndServesNoVersion()
        {
            var path = WriteConfig($"""
                <?config version="{ConfigurableModule.LATEST_VERSION}"?>
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor timeout="5min" keepDisplayAwake="true" /></Environment>
                  <Environment test="false"><SystemMonitor timeout="1min" marker="off" /></Environment>
                  <DefaultEnvironment><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.True(pipeline.Augmenting);

            var configuration = BuildConfiguration(pipeline);

            Assert.Equal("fallback", configuration["marker"]); // inactive block skipped, default merged
            Assert.Null(configuration["version"]); // the format version is the file's header, not configuration data

            var config = StrictConfigurationBinder.Get<SystemMonitorConfig>(configuration, o => o.BindNonPublicProperties = true);

            Assert.NotNull(config);
            Assert.Equal(TimeSpan.FromMinutes(5), config.Timeout);
            Assert.True(config.KeepDisplayAwake);
        }

        [Fact]
        public void Start_UnknownConditionAttribute_Throws()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment powr="ac"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.Start);

            Assert.Contains("powr", ex.Message);
        }

        [Theory]
        [InlineData("true", null)]         // another environment matches -> "else" default is skipped
        [InlineData("false", "fallback")]  // nothing matches -> "else" default is merged
        public void DefaultEnvironment_OnlyIfElse_MergesOnlyWhenNothingMatches(string condition, string? expectedMarker)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Fact]
        public void OnlyIfNever_DisablesTheBlock()
        {
            // the disabled blocks carry a condition attribute nothing registers ("vpn"),
            // proving that conditions of never-blocks are not resolved
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment onlyIf="never" vpn="on"><SystemMonitor marker="disabled" /></Environment>
                  <DefaultEnvironment onlyIf="never"><SystemMonitor fallback="disabled" /></DefaultEnvironment>
                  <Environment test="true"><SystemMonitor timeout="5min" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            var configuration = BuildConfiguration(pipeline);

            Assert.Null(configuration["marker"]);
            Assert.Null(configuration["fallback"]);
            Assert.Equal("5min", configuration["timeout"]);
        }

        [Theory]
        [InlineData("true", null)]        // "home" is applied -> "guest" is suppressed
        [InlineData("false", "guest")]    // "home" is not applied -> "guest" applies
        public void OnlyIfNot_SuppressesWhileTheTargetIsApplied(string condition, string? expectedMarker)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="home" test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="guest" test="true" onlyIfNot="home"><SystemMonitor marker="guest" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Theory]
        [InlineData("true", null)]     // "home" is applied -> "away" is suppressed
        [InlineData("false", "away")]  // "home" is not applied -> "away" applies unconditionally
        public void OnlyIfNot_MayBeTheOnlyAttribute(string condition, string? expectedMarker)
        {
            // a block without condition attributes always matches, so onlyIfNot alone
            // makes it the exact complement of the referenced environment
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="home" test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="away" onlyIfNot="home"><SystemMonitor marker="away" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Fact]
        public void OnlyIfNot_ChainsAcrossEnvironments()
        {
            // "a" applies -> suppresses "b" -> which revives "c"
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment name="a" test="true"><SystemMonitor first="a" /></Environment>
                  <Environment name="b" test="true" onlyIfNot="a"><SystemMonitor second="b" /></Environment>
                  <Environment name="c" test="true" onlyIfNot="b"><SystemMonitor third="c" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            var configuration = BuildConfiguration(pipeline);

            Assert.Equal("a", configuration["first"]);
            Assert.Null(configuration["second"]);
            Assert.Equal("c", configuration["third"]);
        }

        [Theory]
        [InlineData("true", null)]        // "home" is applied -> the default is suppressed
        [InlineData("false", "fallback")] // "home" is not applied -> the default merges
        public void OnlyIfNot_OnDefaultEnvironment_SuppressesTheDefault(string condition, string? expectedMarker)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="home" test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="other" test="true"><SystemMonitor keep="other" /></Environment>
                  <DefaultEnvironment onlyIfNot="home"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            var configuration = BuildConfiguration(pipeline);

            Assert.Equal(expectedMarker, configuration["marker"]);
            Assert.Equal("other", configuration["keep"]);
        }

        [Fact]
        public void OnlyIfNot_ReactsToConditionChangesOfTheTarget()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment name="home" test="toggle"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="away" test="true" onlyIfNot="home"><SystemMonitor timeout="1min" /></Environment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(satisfied: true);
            var (pipeline, monitor) = CreatePipeline(path, toggle);

            pipeline.Start();

            monitor.Reevaluate(); // nothing changed yet
            Assert.False(monitor.ReloadToken.IsCancellationRequested);

            toggle.Satisfied = false; // "home" drops out -> "away" is revived

            monitor.Reevaluate();

            Assert.True(monitor.ReloadToken.IsCancellationRequested);
            Assert.Equal("1min", BuildConfiguration(pipeline)["timeout"]);
        }

        [Theory]
        [InlineData("true", "work")]  // "vpn" is applied -> "work" (onlyIf="vpn") applies too
        [InlineData("false", null)]   // "vpn" is not applied -> "work" cannot apply
        public void OnlyIf_AppliesOnlyWhileTheTargetIsApplied(string condition, string? expectedMarker)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="vpn" test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="work" test="true" onlyIf="vpn"><SystemMonitor marker="work" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Theory]
        [InlineData("true", "work")]  // "vpn" is applied -> "work" applies
        [InlineData("false", null)]   // "vpn" is not applied -> "work" does not
        public void OnlyIf_MayBeTheOnlyAttribute(string condition, string? expectedMarker)
        {
            // a block without condition attributes always matches, so onlyIf alone
            // makes it apply exactly when the referenced environment is applied
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="vpn" test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="work" onlyIf="vpn"><SystemMonitor marker="work" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Theory]
        [InlineData("true", "true", "work")]  // "home" applied AND "work"'s own condition matches -> "work" applies
        [InlineData("true", "false", null)]   // "home" applied but "work"'s own condition fails  -> "work" does not
        [InlineData("false", "true", null)]   // "work"'s condition matches but "home" is not applied -> "work" does not
        public void OnlyIf_CombinesWithItsOwnConditions(string home, string work, string? expectedMarker)
        {
            // onlyIf is ANDed with the block's regular condition attributes: both must hold
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="home" test="{home}"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="work" test="{work}" onlyIf="home"><SystemMonitor marker="work" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Theory]
        [InlineData("true", "a", "b", "c")]      // "a" applies -> enables "b" -> which enables "c"
        [InlineData("false", null, null, null)]  // "a" gone -> the whole chain collapses
        public void OnlyIf_ChainsAcrossEnvironments(string condition, string? first, string? second, string? third)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="a" test="{condition}"><SystemMonitor first="a" /></Environment>
                  <Environment name="b" onlyIf="a"><SystemMonitor second="b" /></Environment>
                  <Environment name="c" onlyIf="b"><SystemMonitor third="c" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            var configuration = BuildConfiguration(pipeline);

            Assert.Equal(first, configuration["first"]);
            Assert.Equal(second, configuration["second"]);
            Assert.Equal(third, configuration["third"]);
        }

        [Theory]
        [InlineData("true", "fallback")]  // "home" is applied -> the default (onlyIf="home") merges
        [InlineData("false", null)]       // "home" is not applied -> the default does not
        public void OnlyIf_OnDefaultEnvironment_GatesTheDefault(string condition, string? expectedMarker)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="home" test="{condition}"><SystemMonitor timeout="5min" /></Environment>
                  <DefaultEnvironment onlyIf="home"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Fact]
        public void OnlyIf_ReactsToConditionChangesOfTheTarget()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment name="vpn" test="toggle"><SystemMonitor timeout="5min" /></Environment>
                  <Environment name="work" test="true" onlyIf="vpn"><SystemMonitor marker="work" /></Environment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(satisfied: false);
            var (pipeline, monitor) = CreatePipeline(path, toggle);

            pipeline.Start();

            monitor.Reevaluate(); // nothing changed yet
            Assert.False(monitor.ReloadToken.IsCancellationRequested);

            toggle.Satisfied = true; // "vpn" comes up -> "work" gains its required environment

            monitor.Reevaluate();

            Assert.True(monitor.ReloadToken.IsCancellationRequested);
            Assert.Equal("work", BuildConfiguration(pipeline)["marker"]);
        }

        [Theory]
        [InlineData("true", "true", null)]     // vpn up, guest up   -> "work" suppressed by onlyIfNot="guest"
        [InlineData("true", "false", "work")]  // vpn up, guest down -> onlyIf met and onlyIfNot clear -> "work" applies
        [InlineData("false", "false", null)]   // vpn down           -> "work" loses its onlyIf requirement
        public void OnlyIf_AndOnlyIfNot_Compose(string vpn, string guest, string? expectedMarker)
        {
            var path = WriteConfig($"""
                <EnvironmentMonitor>
                  <Environment name="vpn" test="{vpn}"><SystemMonitor a="1" /></Environment>
                  <Environment name="guest" test="{guest}"><SystemMonitor b="2" /></Environment>
                  <Environment name="work" onlyIf="vpn" onlyIfNot="guest"><SystemMonitor marker="work" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal(expectedMarker, BuildConfiguration(pipeline)["marker"]);
        }

        [Fact]
        public void HigherPriority_WinsAcrossEnvironments()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor onConflict="error">
                  <Environment test="true" priority="1"><SystemMonitor timeout="5min" /></Environment>
                  <DefaultEnvironment><SystemMonitor timeout="1min" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            // priority resolves the conflict, even under onConflict="error"
            Assert.Equal("5min", BuildConfiguration(pipeline)["timeout"]);
        }

        [Fact]
        public void WriteEffectiveXML_WritesRelativeToConfig_AndRemovesOnDispose()
        {
            var path = WriteConfig($"""
                <?config version="{ConfigurableModule.LATEST_VERSION}"?>
                <EnvironmentMonitor writeEffectiveXML="effective.xml">
                  <DefaultEnvironment><SystemMonitor timeout="5min" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var outputPath = Path.Combine(_directory, "effective.xml");

            var (pipeline, monitor) = CreatePipeline(path);

            using (var exporter = new EffectiveXMLExporter(monitor) { Logger = NullLogger.Instance })
            {
                exporter.Start();

                Assert.False(File.Exists(outputPath)); // written on Start, not before

                pipeline.Start();

                Assert.True(File.Exists(outputPath));

                var document = XDocument.Load(outputPath);
                var effective = document.Root!;

                // the version declared in the authoritative style - the root element's attribute;
                // no <?config?> header (the file is the result of a migration, never migrated itself)
                Assert.DoesNotContain(document.Nodes().OfType<XProcessingInstruction>(),
                    pi => pi.Target.Equals("config", StringComparison.OrdinalIgnoreCase));

                Assert.Equal("SystemMonitor", effective.Name.LocalName);
                Assert.Equal(ConfigurableModule.LATEST_VERSION.ToString(), effective.Attribute("version")?.Value);
                Assert.Equal("5min", effective.Attribute("timeout")?.Value);
            }

            Assert.False(File.Exists(outputPath)); // removed on dispose
        }

        [Fact]
        public void WriteEffectiveXML_AcceptsAbsolutePath()
        {
            var outputPath = Path.Combine(_directory, "sub", "effective.xml");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var path = WriteConfig($"""
                <EnvironmentMonitor writeEffectiveXML="{outputPath}">
                  <DefaultEnvironment><SystemMonitor /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline(path);

            using var exporter = new EffectiveXMLExporter(monitor) { Logger = NullLogger.Instance };
            exporter.Start();

            pipeline.Start();

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void WriteEffectiveXML_IsWrittenForTheInitialConfiguration_WhenTheExporterStartsAfterThePipeline()
        {
            // the production order: Autofac starts the pipeline (a startable) before the
            // exporters come to life - the first effective configuration must not be missed
            var outputPath = Path.Combine(_directory, "effective.xml");

            var path = WriteConfig($"""
                <?config version="{ConfigurableModule.LATEST_VERSION}"?>
                <EnvironmentMonitor writeEffectiveXML="effective.xml">
                  <DefaultEnvironment><SystemMonitor timeout="5min" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline(path);

            pipeline.Start();

            Assert.False(File.Exists(outputPath)); // nobody listening yet

            using var exporter = new EffectiveXMLExporter(monitor) { Logger = NullLogger.Instance };
            exporter.Start();

            Assert.True(File.Exists(outputPath));
            Assert.Equal("5min", XDocument.Load(outputPath).Root!.Attribute("timeout")?.Value);
        }

        [Fact]
        public void Subscribe_ReplaysTheCurrentEffectiveConfiguration_AndThenFollowsTheChanges()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <DefaultEnvironment><SystemMonitor marker="off" /></DefaultEnvironment>
                  <Environment test="toggle"><SystemMonitor marker="on" /></Environment>
                </EnvironmentMonitor>
                """); // document order: the toggled block wins the merge when active

            var toggle = new FakeCondition(satisfied: false);

            var (pipeline, monitor) = CreatePipeline(path, toggle);

            pipeline.Start();

            List<string?> seen = [];

            monitor.Subscribe(effective => seen.Add(effective.Data.Data["marker"]));

            Assert.Equal(["off"], seen); // the configuration published before the subscription

            toggle.Satisfied = true;
            monitor.Reevaluate();

            Assert.Equal(["off", "on"], seen);
        }

        [Fact]
        public void WriteEffectiveXML_MustNotTargetTheConfigFile()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor writeEffectiveXML="monitor.xml">
                  <DefaultEnvironment><SystemMonitor /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            Assert.Throws<ConfigurationValueException>(pipeline.Start);
        }

        [Fact]
        public void Reevaluate_CancelsTheReloadToken_OnlyWhenTheEffectiveConfigChanged()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="toggle"><SystemMonitor marker="on" /></Environment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(satisfied: true);
            var (pipeline, monitor) = CreatePipeline(path, toggle);

            pipeline.Start();

            monitor.Reevaluate(); // the conditions are unchanged -> no reload

            Assert.False(monitor.ReloadToken.IsCancellationRequested);

            toggle.Satisfied = false;

            monitor.Reevaluate(); // the effective configuration changed -> reload

            Assert.True(monitor.ReloadToken.IsCancellationRequested);
        }

        [Fact]
        public void Start_ServesTheEffectiveConfigurationToEveryBuild()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor timeout="5min" marker="on" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="off" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            // the monitor computed the effective config once; every per-build source serves it
            foreach (var _ in Enumerable.Range(0, 2))
            {
                var configuration = BuildConfiguration(pipeline);

                Assert.Equal("on", configuration["marker"]);
                Assert.Equal("5min", configuration["timeout"]);
            }
        }

        [Fact]
        public void Reevaluate_UpdatesTheEffectiveConfig_AndSignalsReload_OnAConditionChange()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="toggle"><SystemMonitor marker="on" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="off" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(satisfied: true);
            var (pipeline, monitor) = CreatePipeline(path, toggle);

            pipeline.Start();

            monitor.ResetReloadToken(); // arm the reload token for this "build"

            toggle.Satisfied = false; // "on" drops out -> the default ("off") applies

            monitor.Reevaluate();

            Assert.True(monitor.ReloadToken.IsCancellationRequested); // the loop rebuilds

            Assert.Equal("off", BuildConfiguration(pipeline)["marker"]);
        }

        [Fact]
        public void Reevaluate_WithoutAChange_DoesNotSignalReload()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="on" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline(path);

            pipeline.Start();

            monitor.ResetReloadToken();

            monitor.Reevaluate();

            Assert.False(monitor.ReloadToken.IsCancellationRequested);
        }

        [Fact]
        public void CheckForChanges_PicksUpEditedEnvironmentBlocks()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <DefaultEnvironment><SystemMonitor marker="before" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline(path);

            pipeline.Start();

            Assert.Equal("before", BuildConfiguration(pipeline)["marker"]);

            // the configuration file is edited between rebuilds
            File.WriteAllText(path, """
                <EnvironmentMonitor>
                  <DefaultEnvironment><SystemMonitor marker="after" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            pipeline.CheckForChanges();

            Assert.Equal("after", BuildConfiguration(pipeline)["marker"]);
        }

        [Fact]
        public void CheckForChanges_WithAnInvalidEdit_IsFatal_ButKeepsServingTheLastGoodConfiguration()
        {
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="good" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline(path);

            pipeline.Start();

            var configuration = BuildConfiguration(pipeline);
            Assert.Equal("good", configuration["marker"]);

            // a well-formed but invalid edit: a condition attribute nothing registers
            File.WriteAllText(path, """
                <EnvironmentMonitor>
                  <Environment nonexistentcondition="x"><SystemMonitor marker="bad" /></Environment>
                </EnvironmentMonitor>
                """);

            // an edit the process cannot apply is fatal by design: exit and let the
            // service manager restart the application
            pipeline.CheckForChanges();

            Assert.True(monitor.ReloadToken.IsCancellationRequested); // the fatal wake-up for the loop

            Assert.Throws<ConfigurationValueException>(pipeline.ThrowIfFailed);

            // until the process exits, the last good configuration stays served
            Assert.Equal("good", configuration["marker"]);
        }

        [Fact]
        public void ArmReload_ArmsAFreshReloadToken_ForEachBuild()
        {
            // the boot window: a token armed for one build is independent of the next, so a change
            // that lands during a build cancels that build's token and is never lost
            var path = WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="toggle"><SystemMonitor marker="on" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="off" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(satisfied: true);
            var (pipeline, monitor) = CreatePipeline(path, toggle);

            pipeline.Start();

            monitor.ResetReloadToken();
            var first = monitor.ReloadToken;

            toggle.Satisfied = false;
            monitor.Reevaluate(); // cancels the first build's token

            Assert.True(first.IsCancellationRequested);

            // the next build arms a fresh, uncancelled token
            monitor.ResetReloadToken();
            Assert.False(monitor.ReloadToken.IsCancellationRequested);
        }

        private sealed class FakeCondition(bool satisfied) : IEnvironmentCondition
        {
            public bool Satisfied { get; set; } = satisfied;

            public bool IsSatisfied() => Satisfied;

            public event EventHandler? Changed { add { } remove { } }
        }

        /// <summary>A condition scope the way the ApplicationBuilder provides one: the
        /// "test" attribute keyed as a named registration, the value as a parameter.</summary>
        private static ILifetimeScope Conditions(FakeCondition? toggle = null)
        {
            var builder = new ContainerBuilder();

            builder.Register((_, parameters) => parameters.TypedAs<string>() switch
                {
                    "toggle" => toggle ?? throw new InvalidOperationException("no toggle condition provided"),
                    string value => (IEnvironmentCondition)new FakeCondition(value == "true"),
                })
                .Named<IEnvironmentCondition>("test");

            return builder.Build();
        }
    }
}
