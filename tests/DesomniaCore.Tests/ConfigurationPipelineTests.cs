using Autofac;
using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Xml.Linq;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The configuration pipeline: mode detection, the change pump into the monitor, and
    /// the fatal-exit policy for changes the running process cannot apply (bad edits, a
    /// switched root). The &lt;?system?&gt; directives are the root host's configuration,
    /// not the pipeline's business - a change of them is neither fatal nor a rebuild.
    /// </summary>
    public class ConfigurationPipelineTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;
        private readonly string _path;

        public ConfigurationPipelineTests() => _path = Path.Combine(_directory, "monitor.xml");

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private void WriteConfig(string content) => File.WriteAllText(_path, content);

        #region Harness

        private sealed class FakeCondition(bool satisfied) : IEnvironmentCondition
        {
            public bool Satisfied { get; set; } = satisfied;

            public bool IsSatisfied() => Satisfied;

            public event EventHandler? Changed { add { } remove { } }
        }

        private static ILifetimeScope Conditions(FakeCondition? toggle = null, Action? onResolve = null)
        {
            var builder = new ContainerBuilder();

            builder.Register((_, parameters) =>
                {
                    onResolve?.Invoke();

                    return parameters.TypedAs<string>() switch
                    {
                        "toggle" => toggle ?? throw new InvalidOperationException("no toggle condition provided"),
                        string value => (IEnvironmentCondition)new FakeCondition(value == "true"),
                    };
                })
                .Named<IEnvironmentCondition>("test");

            return builder.Build();
        }

        private (ConfigurationPipeline Pipeline, EnvironmentMonitor Monitor) CreatePipeline(FakeCondition? toggle = null)
        {
            var source = new ExtendedXmlConfigurationSource(_path);

            var monitor = new EnvironmentMonitor { Logger = NullLogger.Instance };

            var registry = new VersionedModuleRegistry();
            registry.Lock();

            // no migration layer attached: the pipeline works (version check included) without one
            var pipeline = new ConfigurationPipeline(source, monitor, Conditions(toggle), registry)
            {
                Logger = NullLogger.Instance,
            };

            return (pipeline, monitor);
        }

        #endregion

        #region Passthrough mode

        [Fact]
        internal void Passthrough_FileChange_SignalsTheReload()
        {
            WriteConfig("""<SystemMonitor />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            Assert.False(pipeline.Augmenting);

            var token = monitor.ReloadToken;

            WriteConfig("""<SystemMonitor timeout="00:10:00" />""");
            pipeline.CheckForChanges();

            Assert.True(token.IsCancellationRequested);

            pipeline.ThrowIfFailed(); // a plain edit is not fatal
        }

        [Fact]
        internal void Passthrough_UnchangedContent_DoesNotReload()
        {
            WriteConfig("""<SystemMonitor />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var token = monitor.ReloadToken;

            WriteConfig("""<SystemMonitor />"""); // a touch, same content
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);
        }

        private sealed class MigratingModule : ConfigurableModule, IXConfigurationMigration
        {
            public int Migrations;

            protected internal override uint MinVersion => 2;

            protected internal override uint MaxVersion => 2;

            void IXConfigurationMigration.Run(XDocument configuration, uint version) => Migrations++;
        }

        // a build that knows format 2: an outdated file is migrated (and written back, in file
        // mode) at Start. The migration layer is composed the way the application builder does
        // it: the algebra, the migrator decorating the source's file provider, both handed to
        // the pipeline - the source itself stays pristine.
        private (ConfigurationPipeline Pipeline, EnvironmentMonitor Monitor, XConfigurationMigrator Migrator, MigratingModule Module) CreateMigratingPipeline(Action? onConditionResolve = null)
        {
            var module = new MigratingModule();

            var registry = new VersionedModuleRegistry { LatestVersion = 2 };
            registry.Register(module);
            registry.Lock();

            var source = new ExtendedXmlConfigurationSource(_path);

            var migrator = new XConfigurationMigrator(registry, () => source.FullPath) { Logger = NullLogger.Instance };

            source.FileProvider = new MigratingFileProvider(source.FileProvider!, migrator, source.Path!);

            var monitor = new EnvironmentMonitor { Logger = NullLogger.Instance };

            // the pipeline receives the version check only - the migration layer stays
            // invisible to it (it reads whatever the decorated provider serves)
            var pipeline = new ConfigurationPipeline(source, monitor, Conditions(onResolve: onConditionResolve), registry) { Logger = NullLogger.Instance };

            return (pipeline, monitor, migrator, module);
        }

        [Fact]
        internal void Passthrough_TheMigratorsOwnFileWrite_IsNotAChange()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");
            string v1Text = File.ReadAllText(_path);

            var (pipeline, monitor, migrator, module) = CreateMigratingPipeline();
            pipeline.Start();

            Assert.Contains("version=\"2\"", File.ReadAllText(_path));
            Assert.Equal(1, module.Migrations);

            var token = monitor.ReloadToken;

            pipeline.CheckForChanges(); // the watcher reports our own write

            Assert.False(token.IsCancellationRequested);
            pipeline.ThrowIfFailed();

            // the v1 text read back while the file holds the written text is a reader that opened
            // the file before the write (not a revert): served from the cache, no second migration
            migrator.Read(v1Text);
            Assert.Equal(1, module.Migrations);

            // and a real edit afterwards is still a change
            WriteConfig(File.ReadAllText(_path).Replace("<SystemMonitor version=\"2\" />", "<SystemMonitor version=\"2\" timeout=\"00:10:00\" />"));
            pipeline.CheckForChanges();

            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        internal void Passthrough_ARevertToTheMigratorsWrittenText_IsAChangeAgain()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");

            var (pipeline, monitor, migrator, _) = CreateMigratingPipeline();
            pipeline.Start();

            string written = File.ReadAllText(_path);
            Assert.Contains("version=\"2\"", written);

            pipeline.CheckForChanges(); // our own write: the new baseline
            Assert.False(monitor.ReloadToken.IsCancellationRequested);

            // a real edit ...
            WriteConfig(written.Replace("<SystemMonitor version=\"2\" />", "<SystemMonitor version=\"2\" timeout=\"00:10:00\" />"));
            pipeline.CheckForChanges();
            Assert.True(monitor.ReloadToken.IsCancellationRequested);
            monitor.ResetReloadToken();

            // ... and its undo (an editor's Ctrl+Z, a restored backup): the file is exactly the
            // written text again, but the running configuration is not - a change like any other
            Assert.False(migrator.IsOwnWrite(written));

            WriteConfig(written);
            pipeline.CheckForChanges();

            Assert.True(monitor.ReloadToken.IsCancellationRequested);
            pipeline.ThrowIfFailed();
        }

        [Fact]
        internal void Passthrough_AMigrationRunOnTheProvidersReload_IsStillAppliedByThePipeline()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");

            var (pipeline, monitor, migrator, module) = CreateMigratingPipeline();
            pipeline.Start();

            string written = File.ReadAllText(_path);
            Assert.Contains("version=\"2\"", written);

            pipeline.CheckForChanges(); // our own write: nothing to reload
            Assert.False(monitor.ReloadToken.IsCancellationRequested);

            // the user edits the file back to version 1 AND changes a value; the providers reload
            // sooner than the pipeline (a shorter delay), so the migration runs on THEIR read and
            // rewrites the file before the pipeline ever sees the edit ...
            string edited = """<?config version="1" autoMigrate="persistent" ?><SystemMonitor timeout="00:10:00" />""";
            WriteConfig(edited);
            migrator.Read(edited);

            Assert.Equal(2, module.Migrations);
            Assert.Contains("timeout=\"00:10:00\"", File.ReadAllText(_path));
            Assert.Contains("version=\"2\"", File.ReadAllText(_path));

            // ... and what the pipeline then finds on disk is the migrator's own write - carrying
            // data it has not applied yet: a change, the application reloads
            pipeline.CheckForChanges();

            Assert.True(monitor.ReloadToken.IsCancellationRequested);
            pipeline.ThrowIfFailed();
        }

        [Fact]
        internal void Passthrough_ARevertToTheOriginalText_MigratesAndWritesAgain()
        {
            WriteConfig("""<?config version="1" autoMigrate="persistent" ?><SystemMonitor />""");
            string original = File.ReadAllText(_path);

            var (pipeline, monitor, _, module) = CreateMigratingPipeline();
            pipeline.Start();

            string written = File.ReadAllText(_path);
            Assert.Contains("version=\"2\"", written);
            Assert.Equal(1, module.Migrations);

            pipeline.CheckForChanges(); // our own write
            var token = monitor.ReloadToken;

            // the user restores the original file (the backup, an editor's undo): the very same
            // text the migration started from - it is migrated and written again ...
            WriteConfig(original);
            pipeline.CheckForChanges();

            Assert.Equal(2, module.Migrations);
            Assert.Equal(written, File.ReadAllText(_path));

            // ... while the data the application runs on is unchanged: no reload
            Assert.False(token.IsCancellationRequested);
            pipeline.ThrowIfFailed();
        }

        [Fact]
        internal void Augmenting_TheMigratorsOwnFileWrite_IsNotAChange()
        {
            WriteConfig("""
                <?config version="1" autoMigrate="persistent" ?><EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="x" /></Environment>
                </EnvironmentMonitor>
                """);

            int materializations = 0; // every Apply of an augmenting file resolves the conditions anew

            var (pipeline, monitor, _, _) = CreateMigratingPipeline(onConditionResolve: () => materializations++);
            pipeline.Start();

            Assert.True(pipeline.Augmenting);
            Assert.Contains("version=\"2\"", File.ReadAllText(_path));
            Assert.Equal(1, materializations);

            var token = monitor.ReloadToken;

            pipeline.CheckForChanges(); // the watcher reports our own write

            Assert.False(token.IsCancellationRequested);
            Assert.Equal(1, materializations); // the SERVED text is identical - not even re-applied
            pipeline.ThrowIfFailed();

            // and a real edit afterwards is still a change
            WriteConfig(File.ReadAllText(_path).Replace("marker=\"x\"", "marker=\"y\""));
            pipeline.CheckForChanges();

            Assert.Equal(2, materializations);
            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        internal void Passthrough_UnchangedData_DoesNotReload()
        {
            WriteConfig("""<SystemMonitor />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var token = monitor.ReloadToken;

            // formatting and comments are not data - the host would read the same configuration
            WriteConfig("""
                <!-- reformatted -->
                <SystemMonitor   />
                """);
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);
        }

        [Fact]
        internal void Passthrough_ChangedSystemDirective_IsNeitherFatalNorARebuild()
        {
            WriteConfig("""
                <?system useDBus="false" ?>
                <SystemMonitor />
                """);

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var token = monitor.ReloadToken;

            // the directives are the root host's configuration: its own nested source follows
            // them (IOptionsMonitor), the application host has nothing to rebuild for
            WriteConfig("""
                <?system useDBus="true" ?>
                <SystemMonitor />
                """);
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);

            pipeline.ThrowIfFailed();

            // ... and a removed directive is no different
            WriteConfig("""<SystemMonitor />""");
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);

            pipeline.ThrowIfFailed();

            // the pipeline still tracks the data: a real edit after that rebuilds
            WriteConfig("""<SystemMonitor timeout="00:10:00" />""");
            pipeline.CheckForChanges();

            Assert.True(token.IsCancellationRequested);
        }

        #endregion

        #region Fatal changes

        [Fact]
        internal void SwitchedRootElement_IsFatal()
        {
            WriteConfig("""<SystemMonitor />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            Assert.True(monitor.ReloadToken.IsCancellationRequested);

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.ThrowIfFailed);
            Assert.Contains("root", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        internal void BadEdit_IsFatal_ButTheLastGoodConfigurationStaysServed()
        {
            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="good" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var configuration = new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();
            Assert.Equal("good", configuration["marker"]);

            WriteConfig("<EnvironmentMonitor version=\"6\"><Environment"); // half-written edit
            pipeline.CheckForChanges();

            Assert.True(monitor.ReloadToken.IsCancellationRequested);
            Assert.Throws<ConfigurationValueException>(pipeline.ThrowIfFailed);

            // whatever still runs keeps binding the last good data until the process exits
            Assert.Equal("good", configuration["marker"]);
        }

        [Fact]
        internal void UnknownConditionInAnEdit_IsFatal()
        {
            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="good" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline();
            pipeline.Start();

            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment nonsense="x"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.ThrowIfFailed);
            Assert.Contains("nonsense", ex.Message);
        }

        #endregion

        #region Augmenting reload & hot reload

        [Fact]
        internal void FileEdit_PumpsNewEnvironmentsIntoTheMonitor()
        {
            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="before" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var configuration = new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();
            var token = monitor.ReloadToken;

            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="after" /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            Assert.True(token.IsCancellationRequested);

            // the monitor's source hot-reloads: the already-built configuration sees the change
            Assert.Equal("after", configuration["marker"]);
        }

        [Fact]
        internal void FileEdit_WithoutEffectiveChange_DoesNotReload()
        {
            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var token = monitor.ReloadToken;

            // a comment-only edit: the text differs, the effective configuration does not
            WriteConfig("""
                <EnvironmentMonitor>
                  <!-- cosmetics -->
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);

            // neither does an added <?system?> directive (the root host's configuration)
            WriteConfig("""
                <?system useDBus="true" ?>
                <EnvironmentMonitor>
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);

            pipeline.ThrowIfFailed();
        }

        [Fact]
        internal void ConditionChange_HotReloadsTheBuiltConfiguration()
        {
            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment name="on" test="toggle"><SystemMonitor marker="active" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(true);

            var (pipeline, monitor) = CreatePipeline(toggle);
            pipeline.Start();

            var configuration = new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();
            Assert.Equal("active", configuration["marker"]);

            var token = monitor.ReloadToken;

            toggle.Satisfied = false;
            monitor.Reevaluate();

            Assert.True(token.IsCancellationRequested);
            Assert.Equal("fallback", configuration["marker"]);
        }

        [Fact]
        internal void SettingsOnlyEdit_ReExports_WithoutARebuild()
        {
            var outA = Path.Combine(_directory, "a.xml");
            var outB = Path.Combine(_directory, "b.xml");

            WriteConfig("""
                <EnvironmentMonitor writeEffectiveXML="a.xml">
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();

            using var exporter = new Environments.Export.EffectiveXMLExporter(monitor) { Logger = NullLogger.Instance };
            exporter.Start();

            pipeline.Start();

            Assert.True(File.Exists(outA));

            var token = monitor.ReloadToken;

            // move the output path without changing the effective content
            WriteConfig("""
                <EnvironmentMonitor writeEffectiveXML="b.xml">
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            Assert.False(File.Exists(outA)); // the old export is retired
            Assert.True(File.Exists(outB));  // the new one written immediately

            Assert.False(token.IsCancellationRequested); // no rebuild for a settings-only edit
        }

        public sealed class TestOptions
        {
            public string? Marker { get; set; }
        }

        [Fact]
        internal void OptionsMonitor_HotReloads_FromTheMonitorsSource()
        {
            WriteConfig("""
                <EnvironmentMonitor>
                  <Environment name="on" test="toggle"><SystemMonitor marker="active" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            var toggle = new FakeCondition(true);

            var (pipeline, monitor) = CreatePipeline(toggle);
            pipeline.Start();

            var configuration = new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();

            // the standard options stack over the monitor's source - the drop-in claim
            var services = new ServiceCollection();
            services.AddOptions();
            services.Configure<TestOptions>(configuration);

            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptionsMonitor<TestOptions>>();

            Assert.Equal("active", options.CurrentValue.Marker);

            toggle.Satisfied = false;
            monitor.Reevaluate();

            Assert.Equal("fallback", options.CurrentValue.Marker);
        }

        #endregion
    }
}
