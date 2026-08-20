using Autofac;
using MadWizard.Desomnia.Application;
using MadWizard.Desomnia.Application.Shutdown;
using MadWizard.Desomnia.Configuration.Migration;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// End-to-end lifecycle of the persistent container inside the
    /// <see cref="ApplicationBuilder"/>: LoadOnce runs once per module, its registrations
    /// survive a configuration rebuild, and only disposing the builder disposes them.
    /// </summary>
    public class ApplicationBuilderPersistenceTests : IDisposable
    {
        private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"desomnia-test-{Guid.NewGuid():N}.xml");

        public ApplicationBuilderPersistenceTests()
        {
            File.WriteAllText(_configPath, $"""<?config version="{ConfigurableModule.LATEST_VERSION}"?><SystemMonitor timeout="00:10:00" />""");
        }

        public void Dispose() => File.Delete(_configPath);

        public interface IPersistentService { }

        public sealed class PersistentService : IPersistentService, IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        private sealed class PersistentModule : Module
        {
            public int LoadOnceCalls { get; private set; }

            protected internal override void LoadOnce(ContainerBuilder builder)
            {
                LoadOnceCalls++;

                builder.RegisterType<PersistentService>().As<IPersistentService>().SingleInstance();
            }
        }

        public sealed class SecondPersistentService : IPersistentService { }

        public sealed class PersistentCollectionConsumer(IEnumerable<IPersistentService> services)
        {
            public IReadOnlyList<IPersistentService> Services { get; } = [.. services];
        }

        private sealed class CollectionPersistentModule : Module
        {
            protected internal override void LoadOnce(ContainerBuilder builder)
            {
                builder.RegisterType<PersistentService>().As<IPersistentService>().SingleInstance();

                // injects IEnumerable<IPersistentService> INSIDE the persistent container,
                // materializing relationship registrations in its registry
                builder.RegisterType<PersistentCollectionConsumer>().AsSelf().SingleInstance();
            }
        }

        private sealed class AppServiceModule : Module
        {
            protected override void Load(ContainerBuilder builder)
                => builder.RegisterType<SecondPersistentService>().As<IPersistentService>().SingleInstance();
        }

        public sealed class TestConfig
        {
            public TimeSpan? Timeout { get; set; }

            public string? Marker { get; set; }
        }

        private sealed class ConfigurableTestModule : ConfigurableModule<TestConfig>
        {
            public List<TestConfig> Received { get; } = [];

            protected override void Load(ContainerBuilder builder, TestConfig config) => Received.Add(config);
        }

        // the real builders are platform subclasses (DesomniaWindowsServiceBuilder, ...)
        private sealed class SubclassBuilder(string configPath) : ApplicationBuilder(configPath);

        [Fact]
        public void PersistentHost_DoesNotExposeTheBuilderAsAService()
        {
            // the loop lives in the ApplicationHost wrapper, which receives the builder through
            // its constructor: nothing resolves the builder from a container anymore, and no
            // registration may leak it back in (upper layers must not see the framework)
            var builder = new SubclassBuilder(_configPath);

            using ApplicationHost host = builder.Build();

            Assert.Null(host.Services.GetService(typeof(ApplicationBuilder)));
            Assert.Null(host.Services.GetService(typeof(SubclassBuilder)));
        }

        [Fact]
        public void LoadOnce_RunsOncePerModule_AndItsServicesSurviveARebuild()
        {
            var module = new PersistentModule();

            IPersistentService first, second;

            var builder = new ApplicationBuilder(_configPath);
            {
                builder.RegisterModule(module);

                using var host = builder.Build(); // the persistent host, disposed with the block

                using (var app1 = builder.BuildApplication())
                    first = (IPersistentService)app1.Services.GetService(typeof(IPersistentService))!;

                using (var app2 = builder.BuildApplication())
                    second = (IPersistentService)app2.Services.GetService(typeof(IPersistentService))!;

                Assert.Equal(1, module.LoadOnceCalls);
                Assert.NotNull(first);
                Assert.Same(first, second);

                // the application containers are gone, the persistent instance is not
                Assert.False(((PersistentService)first).Disposed);
            }

            // disposing the persistent host disposes its container - and with it the instance
            Assert.True(((PersistentService)first).Disposed);
        }

        [Fact]
        public void RelationshipTypesUsedInsideThePersistentContainer_DoNotShadowAppRegistrationsOnRebuild()
        {
            var builder = new ApplicationBuilder(_configPath);

            builder.RegisterModule(new CollectionPersistentModule());
            builder.RegisterModule(new AppServiceModule());

            builder.Build();

            // first build: activating the consumer resolves IEnumerable<IPersistentService>
            // against the persistent container, growing its registry with relationship
            // registrations — which must NOT drift into the bridged export set
            using (var app1 = builder.BuildApplication())
            {
                var consumer = (PersistentCollectionConsumer)app1.Services.GetService(typeof(PersistentCollectionConsumer))!;
                Assert.Single(consumer.Services); // the persistent scope sees only its own services
            }

            // rebuild: the app-side registration must still join the collection
            using (var app2 = builder.BuildApplication())
            {
                var services = (IEnumerable<IPersistentService>)app2.Services.GetService(typeof(IEnumerable<IPersistentService>))!;
                Assert.Equal(2, services.Count());
            }
        }

        public interface IFakeProbe { bool On { get; } }

        public sealed class FakeProbe : IFakeProbe
        {
            public bool On => true;
        }

        public sealed class ProbedCondition(string value) : Environments.IEnvironmentCondition
        {
            public required IFakeProbe Probe { private get; init; }

            public bool IsSatisfied() => Probe.On && value == "on";

            public event EventHandler? Changed { add { } remove { } }
        }

        private sealed class ConditionModule : Module
        {
            protected internal override void LoadOnce(ContainerBuilder builder)
            {
                builder.RegisterType<FakeProbe>().As<IFakeProbe>().SingleInstance();

                builder.RegisterType<ProbedCondition>().Named<Environments.IEnvironmentCondition>("probed");
            }
        }

        [Fact]
        public void EnvironmentConditions_ResolveFromThePersistentContainer_WithInjectedDependencies()
        {
            var envPath = Path.Combine(Path.GetTempPath(), $"desomnia-env-{Guid.NewGuid():N}.xml");

            File.WriteAllText(envPath, """
                <EnvironmentMonitor>
                  <Environment probed="on"><SystemMonitor marker="matched" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            try
            {
                var module = new ConfigurableTestModule();

                var builder = new ApplicationBuilder(envPath);

                builder.RegisterModule(new ConditionModule());
                builder.RegisterModule(module);

                builder.Build();

                using (var app = builder.BuildApplication()) { }

                // the condition resolved out of the persistent container (probe injected as a
                // required property, attribute value as constructor parameter) and matched
                Assert.Equal("matched", Assert.Single(module.Received).Marker);
            }
            finally
            {
                File.Delete(envPath);
            }
        }

        [Fact]
        public void ConfigurableModule_ReceivesFreshlyBoundConfigForEveryBuild()
        {
            var module = new ConfigurableTestModule();

            var builder = new ApplicationBuilder(_configPath);

            builder.RegisterModule(module);

            builder.Build();

            using (var app1 = builder.BuildApplication()) { }
            using (var app2 = builder.BuildApplication()) { }

            Assert.Equal(2, module.Received.Count);
            Assert.All(module.Received, config => Assert.Equal(TimeSpan.FromMinutes(10), config.Timeout));
            Assert.NotSame(module.Received[0], module.Received[1]);
        }

        #region Configuration format

        // a module that last changed its format in a version this build does not know yet
        private sealed class DemandingModule : ConfigurableModule
        {
            protected internal override uint MinVersion => ConfigurableModule.LATEST_VERSION + 1;
        }

        // a strict plugin that accepts no format at all - the extreme of "limits the format"
        private sealed class StrictPluginModule : ConfigurableModule
        {
            protected internal override uint MaxVersion => 0;
        }

        // the root file provider wraps a load failure; the cause is the migrator's
        private static ConfigurationMigrationException MigrationFailure(Action build)
        {
            var ex = Assert.ThrowsAny<Exception>(build);

            return ex as ConfigurationMigrationException
                ?? Assert.IsType<ConfigurationMigrationException>(Assert.IsType<InvalidDataException>(ex).InnerException);
        }

        [Fact]
        public void Build_RegistersTheModulesWithTheMigrator_ADemandingModuleIsRefused()
        {
            var builder = new ApplicationBuilder(_configPath);

            builder.RegisterModule(new DemandingModule());

            var ex = MigrationFailure(() => builder.Build());

            Assert.Contains(nameof(DemandingModule), ex.Message);
            Assert.Contains($"requires version {ConfigurableModule.LATEST_VERSION + 1}", ex.Message);
        }

        [Fact]
        public void Build_RegistersTheModulesWithTheMigrator_AStrictPluginLimitsTheFormat()
        {
            var builder = new ApplicationBuilder(_configPath);

            builder.RegisterModule(new StrictPluginModule());
            builder.RegisterModule(new ConfigurableTestModule()); // requires 1 > supported 0

            var ex = MigrationFailure(() => builder.Build());

            Assert.Contains(nameof(StrictPluginModule), ex.Message);
            Assert.Contains("supports at most version 0", ex.Message);
        }

        #endregion
    }
}
