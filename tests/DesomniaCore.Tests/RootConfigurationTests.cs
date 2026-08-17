using Autofac;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The root (process-lifetime) configuration: the <c>&lt;?global?&gt;</c> directives are
    /// served by the physical source's nested root source (<see cref="IRootConfigurationSource"/>),
    /// which the root host's builder consumes like any other source - so the modules see them
    /// through the builder in BuildOnce (and as LoadOnce's convenience argument), the persistent
    /// container binds them through the standard options interfaces, and an IOptionsMonitor
    /// follows the file. All of it stays a completely optional feature.
    /// </summary>
    public class RootConfigurationTests : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("DesomniaTests").FullName;
        private readonly string _configPath;

        public RootConfigurationTests() => _configPath = Path.Combine(_directory, "monitor.xml");

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private void WriteConfig(string content) => File.WriteAllText(_configPath, content);

        #region Harness

        private sealed class CapturingModule : ConfigurableModule
        {
            public List<string> Calls { get; } = [];

            public IConfiguration? BuiltWith { get; private set; }

            public IConfiguration? Received { get; private set; }

            protected internal override void BuildOnce(HostApplicationBuilder builder)
            {
                Calls.Add(nameof(BuildOnce));

                BuiltWith = builder.Configuration;
            }

            protected override void LoadOnce(ContainerBuilder builder, IConfiguration config)
            {
                Calls.Add(nameof(LoadOnce));

                Received = config;
            }
        }

        public sealed class FakePlatformConfig
        {
            public bool UseDBus { get; set; } = true;

            public TimeSpan? PollInterval { get; set; }
        }

        private sealed class BindingModule : ConfigurableModule
        {
            public FakePlatformConfig? Bound { get; private set; }

            protected override void LoadOnce(ContainerBuilder builder, IConfiguration config)
                => Bound = Bind<FakePlatformConfig>(config);
        }

        /// <summary>The options way: the root configuration wired into the hosting framework's
        /// options system in BuildOnce, consumed by the persistent container.</summary>
        private sealed class OptionsModule : Module
        {
            protected internal override void BuildOnce(HostApplicationBuilder builder)
                => builder.Services.Configure<FakePlatformConfig>(builder.Configuration.GetSection("Platform"));
        }

        /// <summary>The same, bound the modules' way (strict binder, value variations).</summary>
        private sealed class StrictOptionsModule : Module
        {
            protected internal override void BuildOnce(HostApplicationBuilder builder)
                => builder.Services.ConfigureStrict<FakePlatformConfig>(builder.Configuration.GetSection("Platform"));
        }

        private sealed class TestBuilder(string configPath) : ApplicationBuilder(configPath)
        {
            public void EnableAutoReload() => _source.ReloadOnChange = true;

            /// <summary>Releases the (shared) physical file provider's watcher, so the test
            /// directory can be deleted.</summary>
            public void ReleaseFileWatcher() => (_source.FileProvider as IDisposable)?.Dispose();
        }

        private static IConfigurationRoot RootConfiguration(string path)
            => new ConfigurationBuilder().Add(new ExtendedXmlConfigurationSource(path).RootSource).Build();

        #endregion

        #region Reaching the modules

        [Fact]
        public void GlobalDirectives_ReachBuildOnce_ThroughTheHostBuildersConfiguration_AndLoadOnce()
        {
            WriteConfig("""
                <?global useDBus="false" ?>
                <?global pollInterval="90s" ?>
                <SystemMonitor version="6" />
                """);

            var module = new CapturingModule();

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(module);
            using var host = builder.Build();

            // BuildOnce sees the root host's configuration before the container is built ...
            Assert.Equal([nameof(Module.BuildOnce), nameof(Module.LoadOnce)], module.Calls);

            Assert.NotNull(module.BuiltWith);
            Assert.Equal("false", module.BuiltWith["useDBus"]);
            Assert.Equal("90s", module.BuiltWith["pollInterval"]);

            // ... and LoadOnce's convenience argument is that very configuration
            Assert.Same(module.BuiltWith, module.Received);

            // which is also the persistent container's IConfiguration - the root host owns it
            Assert.Same(module.BuiltWith, host.Services.GetRequiredService<IConfiguration>());
        }

        [Fact]
        public void WithoutDirectives_LoadOnce_ReceivesAnEmptyConfiguration_AndBindingYieldsDefaults()
        {
            WriteConfig("""<SystemMonitor version="6" />""");

            var module = new BindingModule();

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(module);
            using var host = builder.Build();

            Assert.NotNull(module.Bound);
            Assert.True(module.Bound!.UseDBus);         // the type's default
            Assert.Null(module.Bound.PollInterval);
        }

        [Fact]
        public void Options_BindFromTheRootConfiguration_InThePersistentContainer()
        {
            WriteConfig("""
                <?global Platform:useDBus="false" ?>
                <SystemMonitor version="6" />
                """);

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(new OptionsModule());
            using var host = builder.Build();

            Assert.False(host.Services.GetRequiredService<IOptions<FakePlatformConfig>>().Value.UseDBus);
            Assert.False(host.Services.GetRequiredService<IOptionsMonitor<FakePlatformConfig>>().CurrentValue.UseDBus);
        }

        [Fact]
        public void StrictOptions_BindTheRootConfiguration_TheModulesWay_AndFollowAReload()
        {
            WriteConfig("""
                <?global Platform:useDBus="false" ?>
                <?global Platform:pollInterval="2s" ?>
                <SystemMonitor version="6" />
                """);

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(new StrictOptionsModule());
            using var host = builder.Build();

            var monitor = host.Services.GetRequiredService<IOptionsMonitor<FakePlatformConfig>>();

            Assert.False(monitor.CurrentValue.UseDBus);
            Assert.Equal(TimeSpan.FromSeconds(2), monitor.CurrentValue.PollInterval); // "2s" - the value variations

            WriteConfig("""
                <?global Platform:pollInterval="5min" ?>
                <SystemMonitor version="6" />
                """);
            ((IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>()).Reload();

            Assert.True(monitor.CurrentValue.UseDBus);                                 // removed: back to the default
            Assert.Equal(TimeSpan.FromMinutes(5), monitor.CurrentValue.PollInterval);
        }

        #endregion

        #region The nested source

        [Fact]
        public void GlobalDirectives_WorkBelowAnEnvironmentMonitorRoot_Too()
        {
            WriteConfig("""
                <?global marker="here" ?>
                <EnvironmentMonitor version="6">
                  <Environment test="on"><SystemMonitor /></Environment>
                </EnvironmentMonitor>
                """);

            var configuration = RootConfiguration(_configPath);

            var entry = Assert.Single(configuration.AsEnumerable());
            Assert.Equal("marker", entry.Key);
            Assert.Equal("here", entry.Value);
        }

        [Fact]
        public void Directives_BindThroughTheStrictBinder_WithPathsAndValueVariations()
        {
            WriteConfig("""
                <?global Platform:useDBus="false" ?>
                <?global Platform:pollInterval="2s" ?>
                <SystemMonitor version="6" />
                """);

            var configuration = RootConfiguration(_configPath);

            var config = StrictConfigurationBinder.Get<FakePlatformConfig>(
                configuration.GetSection("Platform"), opt => opt.BindNonPublicProperties = true);

            Assert.NotNull(config);
            Assert.False(config!.UseDBus);
            Assert.Equal(TimeSpan.FromSeconds(2), config.PollInterval); // "2s" via ValueVariations
        }

        [Fact]
        public void TheRootElement_IsNotPartOfTheRootConfiguration()
        {
            WriteConfig("""
                <?global marker="here" ?>
                <SystemMonitor version="6"><Section value="x" /></SystemMonitor>
                """);

            var configuration = RootConfiguration(_configPath);

            Assert.Equal("here", configuration["marker"]);
            Assert.Null(configuration["version"]);
            Assert.Null(configuration["Section:value"]);
        }

        [Fact]
        public void DuplicateDirectiveKeys_AreAConfigurationError()
        {
            WriteConfig("""
                <?global useDBus="false" ?>
                <?global USEDBUS="true" ?>
                <SystemMonitor version="6" />
                """);

            // the stock file provider wraps a load failure; the cause is ours
            var ex = Assert.Throws<InvalidDataException>(() => RootConfiguration(_configPath));
            Assert.IsType<ConfigurationValueException>(ex.InnerException);
        }

        [Fact]
        public void MissingFile_YieldsAnEmptyRootConfiguration()
        {
            // the missing file is the effective configuration's (non-optional) business
            var configuration = RootConfiguration(Path.Combine(_directory, "missing.xml"));

            Assert.Empty(configuration.AsEnumerable());
        }

        [Fact]
        public void RootSource_FollowsTheParentsReloadOnChange_AsOfBuildTime()
        {
            WriteConfig("""<SystemMonitor version="6" />""");

            var source = new ExtendedXmlConfigurationSource(_configPath);

            Assert.False(Provider(source).Source.ReloadOnChange);

            source.ReloadOnChange = true; // decided after construction (the command line)

            var provider = Provider(source);

            Assert.True(provider.Source.ReloadOnChange);
            Assert.Same(source.FileProvider, provider.Source.FileProvider); // one watch for both
            Assert.True(provider.Source.Optional);

            static FileConfigurationProvider Provider(ExtendedXmlConfigurationSource source)
                => Assert.IsAssignableFrom<FileConfigurationProvider>(source.RootSource.Build(new ConfigurationBuilder()));
        }

        #endregion

        #region Reload

        [Fact]
        public void OptionsMonitor_FollowsAReloadOfTheRootConfiguration()
        {
            WriteConfig("""
                <?global Platform:useDBus="false" ?>
                <SystemMonitor version="6" />
                """);

            var builder = new ApplicationBuilder(_configPath);
            builder.RegisterModule(new OptionsModule());
            using var host = builder.Build();

            var monitor = host.Services.GetRequiredService<IOptionsMonitor<FakePlatformConfig>>();

            Assert.False(monitor.CurrentValue.UseDBus);

            FakePlatformConfig? changed = null;
            using var subscription = monitor.OnChange(options => changed = options);

            WriteConfig("""
                <?global Platform:useDBus="true" ?>
                <SystemMonitor version="6" />
                """);

            // the root host's configuration is the authority - a reload of it is all it takes
            ((IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>()).Reload();

            Assert.True(monitor.CurrentValue.UseDBus);
            Assert.NotNull(changed);
            Assert.True(changed!.UseDBus);
        }

        [Fact]
        public void BadEdit_KeepsTheLastGoodRootConfiguration()
        {
            WriteConfig("""
                <?global Platform:useDBus="false" ?>
                <SystemMonitor version="6" />
                """);

            var configuration = RootConfiguration(_configPath);

            Assert.Equal("false", configuration["Platform:useDBus"]);

            WriteConfig("""<?global Platform:useDBus="true" ?><SystemMonitor version="6" >""");
            configuration.Reload();

            // the effective configuration's reload is the one that reports the edit
            Assert.Equal("false", configuration["Platform:useDBus"]);
        }

        [Fact]
        public async Task WithAutoReload_AnEditOfTheFile_ReachesTheOptionsMonitor()
        {
            WriteConfig("""
                <?global Platform:useDBus="false" ?>
                <SystemMonitor version="6" />
                """);

            var builder = new TestBuilder(_configPath);
            builder.EnableAutoReload();
            builder.RegisterModule(new OptionsModule());

            try
            {
                using var host = builder.Build();

                var monitor = host.Services.GetRequiredService<IOptionsMonitor<FakePlatformConfig>>();

                Assert.False(monitor.CurrentValue.UseDBus);

                TaskCompletionSource<bool> changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var subscription = monitor.OnChange(options => changed.TrySetResult(options.UseDBus));

                WriteConfig("""
                    <?global Platform:useDBus="true" ?>
                    <SystemMonitor version="6" />
                    """);

                Assert.True(await changed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.True(monitor.CurrentValue.UseDBus);
            }
            finally
            {
                builder.ReleaseFileWatcher();
            }
        }

        #endregion
    }
}
