using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The configuration pipeline: mode detection, the change pump into the monitor, and
    /// the fatal-exit policy for changes the running process cannot apply (bad edits,
    /// changed &lt;?global?&gt; directives, a switched root).
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

        private (ConfigurationPipeline Pipeline, EnvironmentMonitor Monitor) CreatePipeline(
            FakeCondition? toggle = null, PersistentConfiguration? persistent = null)
        {
            var source = new ExtendedXmlConfigurationSource(_path);

            var monitor = new EnvironmentMonitor { Logger = NullLogger.Instance };

            var pipeline = new ConfigurationPipeline(source, _path,
                persistent ?? PersistentConfiguration.Empty, monitor, Conditions(toggle))
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
            WriteConfig("""<SystemMonitor version="6" />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            Assert.False(pipeline.Augmenting);

            var token = monitor.ReloadToken;

            WriteConfig("""<SystemMonitor version="6" timeout="00:10:00" />""");
            pipeline.CheckForChanges();

            Assert.True(token.IsCancellationRequested);

            pipeline.ThrowIfFailed(); // a plain edit is not fatal
        }

        [Fact]
        internal void Passthrough_UnchangedContent_DoesNotReload()
        {
            WriteConfig("""<SystemMonitor version="6" />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var token = monitor.ReloadToken;

            WriteConfig("""<SystemMonitor version="6" />"""); // a touch, same content
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);
        }

        #endregion

        #region Fatal changes

        [Fact]
        internal void ChangedGlobalDirective_IsFatal()
        {
            WriteConfig("""
                <?global useDBus="false" ?>
                <SystemMonitor version="6" />
                """);

            var source = new ExtendedXmlConfigurationSource(_path);
            var persistent = PersistentConfiguration.LoadFrom(source);

            var (pipeline, monitor) = CreatePipeline(persistent: persistent);
            pipeline.Start();

            var token = monitor.ReloadToken;

            WriteConfig("""
                <?global useDBus="true" ?>
                <SystemMonitor version="6" />
                """);
            pipeline.CheckForChanges();

            Assert.True(token.IsCancellationRequested); // the fatal wake-up for the loop

            var ex = Assert.Throws<ConfigurationValueException>(pipeline.ThrowIfFailed);
            Assert.Contains("persistent", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        internal void RemovedGlobalDirective_IsFatal_Too()
        {
            WriteConfig("""
                <?global useDBus="false" ?>
                <SystemMonitor version="6" />
                """);

            var source = new ExtendedXmlConfigurationSource(_path);

            var (pipeline, _) = CreatePipeline(persistent: PersistentConfiguration.LoadFrom(source));
            pipeline.Start();

            WriteConfig("""<SystemMonitor version="6" />""");
            pipeline.CheckForChanges();

            Assert.Throws<ConfigurationValueException>(pipeline.ThrowIfFailed);
        }

        [Fact]
        internal void SwitchedRootElement_IsFatal()
        {
            WriteConfig("""<SystemMonitor version="6" />""");

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            WriteConfig("""
                <EnvironmentMonitor version="6">
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
                <EnvironmentMonitor version="6">
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
                <EnvironmentMonitor version="6">
                  <Environment test="true"><SystemMonitor marker="good" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, _) = CreatePipeline();
            pipeline.Start();

            WriteConfig("""
                <EnvironmentMonitor version="6">
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
                <EnvironmentMonitor version="6">
                  <Environment test="true"><SystemMonitor marker="before" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var configuration = new ConfigurationBuilder().Add(pipeline.EffectiveSource).Build();
            var token = monitor.ReloadToken;

            WriteConfig("""
                <EnvironmentMonitor version="6">
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
                <EnvironmentMonitor version="6">
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();
            pipeline.Start();

            var token = monitor.ReloadToken;

            // a comment-only edit: the text differs, the effective configuration does not
            WriteConfig("""
                <EnvironmentMonitor version="6">
                  <!-- cosmetics -->
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);
            pipeline.CheckForChanges();

            Assert.False(token.IsCancellationRequested);
        }

        [Fact]
        internal void ConditionChange_HotReloadsTheBuiltConfiguration()
        {
            WriteConfig("""
                <EnvironmentMonitor version="6">
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
                <EnvironmentMonitor version="6" outputEffectiveXML="a.xml">
                  <Environment test="true"><SystemMonitor marker="same" /></Environment>
                </EnvironmentMonitor>
                """);

            var (pipeline, monitor) = CreatePipeline();

            using var exporter = new Environments.Export.EffectiveXmlExporter { Logger = NullLogger.Instance };
            monitor.EffectiveChanged += exporter.Export;

            pipeline.Start();

            Assert.True(File.Exists(outA));

            var token = monitor.ReloadToken;

            // move the output path without changing the effective content
            WriteConfig("""
                <EnvironmentMonitor version="6" outputEffectiveXML="b.xml">
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
                <EnvironmentMonitor version="6">
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
