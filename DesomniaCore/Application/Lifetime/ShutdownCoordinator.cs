using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Core.Resolving.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Application.Lifetime
{
    /// <summary>
    /// Runs the <see cref="IAsyncStoppable"/> teardowns inside the persistent host's stop
    /// phase. As a hosted service its <see cref="StopAsync"/> executes inside
    /// <c>Host.StopAsync</c> — after the loop has drained the application host, before
    /// <c>ApplicationStopped</c> releases the process lifetime — so on Windows the SCM sees
    /// the service stopped only once every teardown has run, bounded by
    /// <c>HostOptions.ShutdownTimeout</c>. Container disposal, which the process lifetime
    /// does NOT wait for, is thereby left with nothing slow to do.
    /// <para>This namespace stays behind the <see cref="FrameworkContainerBridge"/> by
    /// design: the coordinator is persistent-host infrastructure, never an application
    /// service.</para>
    /// </summary>
    internal sealed class ShutdownCoordinator : IHostedService
    {
        public required ILogger Logger { private get; init; }

        private readonly Lock _lock = new();

        private readonly List<IAsyncStoppable> _stoppables = [];

        /// <summary>Called by the resolve middleware whenever the persistent container
        /// activates an <see cref="IAsyncStoppable"/> — including an activation a bridged
        /// resolve from the application container triggers (the lazy first demand).</summary>
        internal void Track(IAsyncStoppable stoppable)
        {
            lock (_lock)
            {
                if (!_stoppables.Contains(stoppable))
                    _stoppables.Add(stoppable);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            IAsyncStoppable[] stoppables;

            lock (_lock)
                stoppables = [.. _stoppables];

            // reverse activation order, sequentially — the stop semantics of hosted services
            for (int i = stoppables.Length - 1; i >= 0; i--)
            {
                try
                {
                    await stoppables[i].StopAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // the shutdown timeout expired; the remaining teardowns would not get
                    // any time either — leave the rest to their disposal backstops
                    Logger.LogWarning($"Teardown of {stoppables[i].GetType().Name} did not finish within the shutdown timeout; abandoning the remaining {i} teardown(s).");

                    return;
                }
                catch (Exception ex)
                {
                    // never let one teardown starve the others (or keep the stop from completing)
                    Logger.LogError(ex, $"Teardown of {stoppables[i].GetType().Name} failed.");
                }
            }
        }
    }

    internal sealed class ShutdownCoordinationModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<ShutdownCoordinator>()
                .As<IHostedService>()
                .SingleInstance()
                .AsSelf();
        }

        protected override void AttachToComponentRegistration(IComponentRegistryBuilder registry, IComponentRegistration registration)
        {
            registration.PipelineBuilding += (sender, pipeline) =>
            {
                pipeline.Use(PipelinePhase.Activation, MiddlewareInsertionMode.EndOfPhase, (context, next) =>
                {
                    next(context);

                    if (context.Instance is IAsyncStoppable stoppable)
                        context.Resolve<ShutdownCoordinator>().Track(stoppable);
                });
            };
        }
    }
}
