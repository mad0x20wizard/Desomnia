using Autofac;
using Autofac.Core;
using Autofac.Features.Metadata;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class PersistentServiceSourceTests
    {
        public interface IAdapter { }

        public sealed class Adapter : IAdapter, IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        public sealed class SecondAdapter : IAdapter { }

        public sealed class StartableAdapter : IAdapter, IStartable
        {
            public int Starts { get; private set; }

            public void Start() => Starts++;
        }

        public sealed class Consumer(IAdapter adapter)
        {
            public IAdapter Adapter => adapter;
        }

        private static IContainer BuildPersistent(Action<ContainerBuilder> configure)
        {
            var builder = new ContainerBuilder();

            configure(builder);

            return builder.Build();
        }

        private static IContainer BuildApplication(IContainer persistent, Action<ContainerBuilder>? configure = null)
        {
            var builder = new ContainerBuilder();

            builder.RegisterSource(new FrameworkContainerBridge(persistent));

            configure?.Invoke(builder);

            return builder.Build();
        }

        [Fact]
        public void BridgesPersistentSingleton_SameInstanceAcrossApplicationContainers()
        {
            using var persistent = BuildPersistent(b => b.RegisterType<Adapter>().As<IAdapter>().SingleInstance());

            using var app1 = BuildApplication(persistent);
            using var app2 = BuildApplication(persistent);

            Assert.Same(app1.Resolve<IAdapter>(), app2.Resolve<IAdapter>());
            Assert.Same(persistent.Resolve<IAdapter>(), app1.Resolve<IAdapter>());
        }

        [Fact]
        public void OnlyIf_IsRegistered_SeesBridgedServiceAtBuildTime()
        {
            using var persistent = BuildPersistent(b => b.RegisterType<Adapter>().As<IAdapter>().SingleInstance());

            using var app = BuildApplication(persistent, b =>
                b.RegisterType<Consumer>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(IAdapter))))
                    .AsSelf()
                    .SingleInstance());

            Assert.True(app.IsRegistered<Consumer>());
            Assert.Same(persistent.Resolve<IAdapter>(), app.Resolve<Consumer>().Adapter);
        }

        [Fact]
        public void OnlyIf_IsRegistered_StaysClosedWithoutBridgedService()
        {
            using var persistent = BuildPersistent(_ => { });

            using var app = BuildApplication(persistent, b =>
                b.RegisterType<Consumer>()
                    .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(IAdapter))))
                    .AsSelf()
                    .SingleInstance());

            Assert.False(app.IsRegistered<Consumer>());
        }

        [Fact]
        public void ApplicationContainerDisposal_LeavesPersistentInstanceAlive()
        {
            using var persistent = BuildPersistent(b => b.RegisterType<Adapter>().As<IAdapter>().SingleInstance());

            Adapter adapter;

            using (var app = BuildApplication(persistent))
            {
                adapter = (Adapter)app.Resolve<IAdapter>();
            }

            Assert.False(adapter.Disposed);
        }

        [Fact]
        public void PersistentContainerDisposal_DisposesTheInstance()
        {
            var persistent = BuildPersistent(b => b.RegisterType<Adapter>().As<IAdapter>().SingleInstance());

            var adapter = (Adapter)persistent.Resolve<IAdapter>();

            persistent.Dispose();

            Assert.True(adapter.Disposed);
        }

        [Fact]
        public void FrameworkRegistrations_AreNotExported()
        {
            // the persistent host also holds framework services now; the bridge keeps them behind
            // by namespace (Microsoft.*/System.*/Autofac.*), so the inner host runs its own
            using var factory = new LoggerFactory();
            using var persistent = BuildPersistent(b =>
                b.RegisterInstance(factory).As<ILoggerFactory>().ExternallyOwned());

            using var app = BuildApplication(persistent);

            Assert.False(app.IsRegistered<ILoggerFactory>());
        }

        [Fact]
        public void ContainerSelfRegistrations_AreNotExported()
        {
            using var persistent = BuildPersistent(_ => { });

            var source = new FrameworkContainerBridge(persistent);

            Assert.Empty(((IRegistrationSource)source).RegistrationsFor(new TypedService(typeof(ILifetimeScope)), _ => []));
            Assert.Empty(((IRegistrationSource)source).RegistrationsFor(new TypedService(typeof(IComponentContext)), _ => []));
        }

        [Fact]
        public void MultipleRegistrations_KeepCollectionOrderAndDefault()
        {
            using var persistent = BuildPersistent(b =>
            {
                b.RegisterType<Adapter>().As<IAdapter>().SingleInstance();
                b.RegisterType<SecondAdapter>().As<IAdapter>().SingleInstance();
            });

            using var app = BuildApplication(persistent);

            // collections keep the persistent registration order, the scalar resolve keeps
            // the persistent default (the LAST registration)
            var adapters = app.Resolve<IEnumerable<IAdapter>>().ToList();

            Assert.Equal(2, adapters.Count);
            Assert.IsType<Adapter>(adapters[0]);
            Assert.IsType<SecondAdapter>(adapters[1]);

            Assert.Same(persistent.Resolve<IAdapter>(), app.Resolve<IAdapter>());
        }

        [Fact]
        public void PersistentStartable_IsNotRestartedByApplicationContainerBuilds()
        {
            using var persistent = BuildPersistent(b =>
                b.RegisterType<StartableAdapter>().As<IAdapter>().As<IStartable>().AsSelf().SingleInstance());

            var adapter = persistent.Resolve<StartableAdapter>();

            Assert.Equal(1, adapter.Starts); // started once, by the persistent build

            using var app1 = BuildApplication(persistent);
            using var app2 = BuildApplication(persistent);

            Assert.Equal(1, adapter.Starts);
            Assert.False(app1.IsRegistered<IStartable>()); // the service itself is not bridged
            Assert.Same(adapter, app1.Resolve<IAdapter>()); // the component still is
        }

        [Fact]
        public void DisposableTransientPersistentRegistrations_AreRejected()
        {
            using var persistent = BuildPersistent(b => b.RegisterType<Adapter>().As<IAdapter>());

            Assert.Throws<InvalidOperationException>(() => FrameworkContainerBridge.ValidateLifetimes(persistent));
        }

        [Fact]
        public void PerScopeLifetimes_AreRejected()
        {
            // non-disposable, so it is the lifetime that trips the check: the instance would
            // diverge between the bridged root and the condition child scopes
            using var persistent = BuildPersistent(b =>
                b.RegisterType<SecondAdapter>().As<IAdapter>().InstancePerLifetimeScope());

            Assert.Throws<InvalidOperationException>(() => FrameworkContainerBridge.ValidateLifetimes(persistent));
        }

        [Fact]
        public void SingletonExternallyOwnedAndStatelessTransientRegistrations_AreAccepted()
        {
            using var persistent = BuildPersistent(b =>
            {
                b.RegisterType<Adapter>().As<IAdapter>().SingleInstance();
                b.RegisterType<Adapter>().AsSelf().ExternallyOwned();
                b.RegisterType<SecondAdapter>().AsSelf(); // non-disposable transient
            });

            FrameworkContainerBridge.ValidateLifetimes(persistent);
        }

        [Fact]
        public void DelegateCreatedDisposableTransient_FailsLoudlyAtBridgedResolve()
        {
            // the delegate only declares IAdapter, so the static check cannot see the
            // disposable product — the bridge must catch it at resolve time instead of
            // letting the persistent root track instances until process exit
            using var persistent = BuildPersistent(b => b.Register(_ => (IAdapter)new Adapter()));

            FrameworkContainerBridge.ValidateLifetimes(persistent); // blind to the delegate

            using var app = BuildApplication(persistent);

            var ex = Assert.Throws<DependencyResolutionException>(() => app.Resolve<IAdapter>());

            Assert.Contains("disposable transient", ex.ToString());
        }

        [Fact]
        public void MetadataOfPersistentRegistrations_IsPreserved()
        {
            using var persistent = BuildPersistent(b =>
                b.RegisterType<Adapter>().As<IAdapter>().WithMetadata("origin", "persistent").SingleInstance());

            using var app = BuildApplication(persistent);

            Assert.Equal("persistent", app.Resolve<Meta<IAdapter>>().Metadata["origin"]);
        }

        [Fact]
        public void KeyedServices_AreBridged()
        {
            using var persistent = BuildPersistent(b =>
                b.RegisterType<Adapter>().Named<IAdapter>("primary").SingleInstance());

            using var app = BuildApplication(persistent);

            Assert.Same(persistent.ResolveNamed<IAdapter>("primary"), app.ResolveNamed<IAdapter>("primary"));
        }
    }
}
