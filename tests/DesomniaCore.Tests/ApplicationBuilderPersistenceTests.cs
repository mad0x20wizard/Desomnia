using Autofac;
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
            File.WriteAllText(_configPath, """<SystemMonitor version="6" timeout="00:10:00" />""");
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
            public int Version { get; set; }

            public TimeSpan? Timeout { get; set; }

            public string? Marker { get; set; }
        }

        private sealed class ConfigurableTestModule : ConfigurableModule<TestConfig>
        {
            public List<TestConfig> Received { get; } = [];

            protected override void Load(ContainerBuilder builder, TestConfig config) => Received.Add(config);
        }

        // the real builders are platform subclasses (DesomniaWindowsServiceBuilder, ...); the
        // persistent registration of the builder must resolve as ApplicationBuilder regardless
        private sealed class SubclassBuilder(string configPath) : ApplicationBuilder(configPath);

        [Fact]
        public void PersistentHost_BuiltViaAPlatformSubclass_ResolvesTheBuilderAsApplicationBuilder()
        {
            // regression: RegisterInstance(this).AsSelf() registered the runtime subclass type, so
            // the rebuild loop's ApplicationBuilder dependency was unresolvable
            using var builder = new SubclassBuilder(_configPath);

            using DesomniaHost host = builder.Build(); // the loop lives in the DesomniaHost wrapper

            Assert.Same(builder, host.Services.GetService(typeof(ApplicationBuilder)));
        }

        [Fact]
        public void LoadOnce_RunsOncePerModule_AndItsServicesSurviveARebuild()
        {
            var module = new PersistentModule();

            IPersistentService first, second;

            using (var builder = new ApplicationBuilder(_configPath))
            {
                builder.RegisterModule(module);

                builder.Build(); // the persistent host, held by the builder

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

            // disposing the builder disposes the persistent host — and with it the container
            Assert.True(((PersistentService)first).Disposed);
        }

        [Fact]
        public void RelationshipTypesUsedInsideThePersistentContainer_DoNotShadowAppRegistrationsOnRebuild()
        {
            using var builder = new ApplicationBuilder(_configPath);

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
                <EnvironmentMonitor version="6">
                  <Environment probed="on"><SystemMonitor marker="matched" /></Environment>
                  <DefaultEnvironment onlyIf="else"><SystemMonitor marker="fallback" /></DefaultEnvironment>
                </EnvironmentMonitor>
                """);

            try
            {
                var module = new ConfigurableTestModule();

                using var builder = new ApplicationBuilder(envPath);

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

            using var builder = new ApplicationBuilder(_configPath);

            builder.RegisterModule(module);

            builder.Build();

            using (var app1 = builder.BuildApplication()) { }
            using (var app2 = builder.BuildApplication()) { }

            Assert.Equal(2, module.Received.Count);
            Assert.All(module.Received, config =>
            {
                Assert.Equal(6, config.Version);
                Assert.Equal(TimeSpan.FromMinutes(10), config.Timeout);
            });
            Assert.NotSame(module.Received[0], module.Received[1]);
        }
    }
}
