using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The persistent (process-lifetime) configuration: <c>&lt;?global?&gt;</c> directives
    /// become an IConfiguration that reaches every module's LoadOnce before the persistent
    /// container is built - and stay a completely optional feature.
    /// </summary>
    public class PersistentConfigurationTests : IDisposable
    {
        private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"desomnia-global-{Guid.NewGuid():N}.xml");

        public void Dispose() => File.Delete(_configPath);

        private sealed class CapturingModule : Module
        {
            public IConfiguration? Received { get; private set; }

            protected internal override void LoadOnce(ContainerBuilder builder, IConfiguration config)
                => Received = config;
        }

        public sealed class FakePlatformConfig
        {
            public bool UseDBus { get; set; } = true;

            public TimeSpan? PollInterval { get; set; }
        }

        private sealed class BindingModule : ConfigurableModule
        {
            public FakePlatformConfig? Bound { get; private set; }

            protected internal override void LoadOnce(ContainerBuilder builder, IConfiguration config)
                => Bound = Bind<FakePlatformConfig>(config);
        }

        [Fact]
        public void GlobalDirectives_ReachLoadOnce_BeforeTheContainerIsBuilt()
        {
            File.WriteAllText(_configPath, """
                <?global useDBus="false" ?>
                <?global pollInterval="90s" ?>
                <SystemMonitor version="6" />
                """);

            var module = new CapturingModule();

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(module);
            builder.Build();

            Assert.NotNull(module.Received);
            Assert.Equal("false", module.Received["useDBus"]);
            Assert.Equal("90s", module.Received["pollInterval"]);
        }

        [Fact]
        public void GlobalDirectives_WorkBelowAnEnvironmentMonitorRoot_Too()
        {
            File.WriteAllText(_configPath, """
                <?global marker="here" ?>
                <EnvironmentMonitor version="6">
                  <Environment test="on"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var source = new ExtendedXmlConfigurationSource(_configPath);

            var entry = Assert.Single(source.LoadPersistentConfiguration());
            Assert.Equal("marker", entry.Key);
            Assert.Equal("here", entry.Value);
        }

        [Fact]
        public void WithoutDirectives_LoadOnce_ReceivesAnEmptyConfiguration_AndBindingYieldsDefaults()
        {
            File.WriteAllText(_configPath, """<SystemMonitor version="6" />""");

            var module = new BindingModule();

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(module);
            builder.Build();

            Assert.NotNull(module.Bound);
            Assert.True(module.Bound!.UseDBus);         // the type's default
            Assert.Null(module.Bound.PollInterval);
        }

        [Fact]
        public void Directives_BindThroughTheStrictBinder_WithPathsAndValueVariations()
        {
            File.WriteAllText(_configPath, """
                <?global Platform:useDBus="false" ?>
                <?global Platform:pollInterval="2s" ?>
                <SystemMonitor version="6" />
                """);

            var source = new ExtendedXmlConfigurationSource(_configPath);

            var persistent = PersistentConfiguration.LoadFrom(source);

            var config = StrictConfigurationBinder.Get<FakePlatformConfig>(
                persistent.Configuration.GetSection("Platform"), opt => opt.BindNonPublicProperties = true);

            Assert.NotNull(config);
            Assert.False(config!.UseDBus);
            Assert.Equal(TimeSpan.FromSeconds(2), config.PollInterval); // "2s" via ValueVariations
        }

        [Fact]
        public void DuplicateDirectiveKeys_AreAConfigurationError()
        {
            File.WriteAllText(_configPath, """
                <?global useDBus="false" ?>
                <?global USEDBUS="true" ?>
                <SystemMonitor version="6" />
                """);

            var source = new ExtendedXmlConfigurationSource(_configPath);

            Assert.Throws<ConfigurationValueException>(() => PersistentConfiguration.LoadFrom(source));
        }

        [Fact]
        public void MissingFile_YieldsAnEmptyPersistentConfiguration()
        {
            var source = new ExtendedXmlConfigurationSource(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xml"));

            var persistent = PersistentConfiguration.LoadFrom(source);

            Assert.True(persistent.IsEmpty);
        }

        [Fact]
        public void ASourceWithoutPersistentSupport_YieldsAnEmptyConfiguration()
        {
            var source = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource();

            Assert.True(PersistentConfiguration.LoadFrom(source).IsEmpty);
        }

        [Theory]
        [InlineData("""<?global a="1" ?>""", true)]                        // identical
        [InlineData("""<?global A="1" ?>""", true)]                        // keys case-insensitive
        [InlineData("""<?global a="2" ?>""", false)]                       // value changed
        [InlineData("""<?global a="1" ?><?global b="2" ?>""", false)]      // entry added
        [InlineData("", false)]                                            // entry removed
        internal void Matches_DetectsChangedDirectives(string directives, bool expected)
        {
            var baseline = new PersistentConfiguration([new("a", "1")]);

            File.WriteAllText(_configPath, $"""
                {directives}
                <SystemMonitor version="6" />
                """);

            var current = new ExtendedXmlConfigurationSource(_configPath).LoadPersistentConfiguration();

            Assert.Equal(expected, baseline.Matches(current));
        }
    }
}
