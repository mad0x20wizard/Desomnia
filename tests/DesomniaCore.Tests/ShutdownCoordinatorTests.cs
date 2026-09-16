using Autofac;
using MadWizard.Desomnia.Application.Shutdown;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class ShutdownCoordinatorTests
    {
        public class Stoppable : IAsyncStoppable
        {
            public static readonly List<Stoppable> StopOrder = [];

            public int Stops { get; private set; }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Stops++;

                StopOrder.Add(this);

                return Task.CompletedTask;
            }
        }

        public sealed class SecondStoppable : Stoppable { }

        public sealed class FaultingStoppable : IAsyncStoppable
        {
            public Task StopAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("teardown failure");
        }

        public sealed class CancellingStoppable : IAsyncStoppable
        {
            public Task StopAsync(CancellationToken cancellationToken) => Task.FromCanceled(new CancellationToken(canceled: true));
        }

        private static IContainer Build(Action<ContainerBuilder>? configure = null)
        {
            var builder = new ContainerBuilder();

            builder.RegisterInstance<ILoggerFactory>(NullLoggerFactory.Instance);

            builder.RegisterModule<LoggingModule>();
            builder.RegisterModule<ShutdownCoordinationModule>();

            configure?.Invoke(builder);

            return builder.Build();
        }

        private static Task Stop(IContainer container, CancellationToken token = default)
            => container.Resolve<IHostedService>().StopAsync(token);

        [Fact]
        public async Task StopsActivatedStoppable()
        {
            using var container = Build(b => b.RegisterType<Stoppable>().SingleInstance());

            var stoppable = container.Resolve<Stoppable>();

            await Stop(container);

            Assert.Equal(1, stoppable.Stops);
        }

        [Fact]
        public async Task NeverActivated_IsNeverStopped_AndNeverInstantiated()
        {
            int activations = 0;

            using var container = Build(b => b.Register(_ => { activations++; return new Stoppable(); }).SingleInstance());

            await Stop(container);

            Assert.Equal(0, activations);
        }

        [Fact]
        public async Task StopsOnlyOnce_ForASingletonResolvedRepeatedly()
        {
            using var container = Build(b => b.RegisterType<Stoppable>().SingleInstance());

            var stoppable = container.Resolve<Stoppable>();
            Assert.Same(stoppable, container.Resolve<Stoppable>());

            await Stop(container);

            Assert.Equal(1, stoppable.Stops);
        }

        [Fact]
        public async Task StopsInReverseActivationOrder()
        {
            using var container = Build(b =>
            {
                b.RegisterType<Stoppable>().SingleInstance();
                b.RegisterType<SecondStoppable>().SingleInstance();
            });

            var first = container.Resolve<Stoppable>();
            var second = container.Resolve<SecondStoppable>();

            Stoppable.StopOrder.Clear();

            await Stop(container);

            Assert.Equal([second, first], Stoppable.StopOrder);
        }

        [Fact]
        public async Task OneFailingTeardown_DoesNotStarveTheOthers()
        {
            using var container = Build(b =>
            {
                b.RegisterType<Stoppable>().SingleInstance();
                b.RegisterType<FaultingStoppable>().SingleInstance();
            });

            var survivor = container.Resolve<Stoppable>();
            container.Resolve<FaultingStoppable>(); // activated after -> stopped (and fails) first

            await Stop(container);

            Assert.Equal(1, survivor.Stops);
        }

        [Fact]
        public async Task ExpiredShutdownTimeout_AbandonsTheRemainingTeardowns()
        {
            using var container = Build(b =>
            {
                b.RegisterType<Stoppable>().SingleInstance();
                b.RegisterType<CancellingStoppable>().SingleInstance();
            });

            var abandoned = container.Resolve<Stoppable>();
            container.Resolve<CancellingStoppable>(); // stopped first, observes the expired token

            using var expired = new CancellationTokenSource();
            expired.Cancel();

            await Stop(container, expired.Token);

            Assert.Equal(0, abandoned.Stops);
        }

        [Fact]
        public async Task ReachesAnActivationTriggeredByABridgedResolve()
        {
            // the lazy first demand: the application container resolves through the bridge,
            // the persistent registration's pipeline activates — and must still be tracked
            using var persistent = Build(b => b.RegisterType<Stoppable>().As<IAsyncStoppable>().AsSelf().SingleInstance());

            var builder = new ContainerBuilder();
            builder.RegisterSource(new RootContainerBridge(persistent));
            using var application = builder.Build();

            var stoppable = application.Resolve<Stoppable>();

            await Stop(persistent);

            Assert.Equal(1, stoppable.Stops);
        }
    }
}
