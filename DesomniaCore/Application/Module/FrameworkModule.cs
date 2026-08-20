using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Application.Shutdown;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Environments.Export;
using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia
{
    /// <summary>
    /// Registers the <see cref="ShutdownCoordinator"/> and the resolve middleware that feeds
    /// it: every component the container activates is inspected once, and an instance
    /// implementing <see cref="IAsyncStoppable"/> is handed to the coordinator. Connection by
    /// activation keeps the components oblivious of the coordinator — and keeps lazy creation
    /// intact, because nothing is resolved that a consumer did not ask for.
    /// Persistent container only: the application containers tear down inside the loop's
    /// drain, which the stop phase already waits for.
    /// </summary>
    internal sealed class FrameworkModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<ShutdownCoordinator>()
                .As<IHostedService>()
                .SingleInstance()
                .AsSelf();

            RegisterEnvironmentMonitor(builder);
        }

        private void RegisterEnvironmentMonitor(ContainerBuilder builder)
        {
            // the configuration authority: it receives the parsed environment blocks from the
            // pipeline and decides the effective configuration the host consumes
            builder.RegisterType<EnvironmentMonitor>()
                .SingleInstance()
                .AsSelf();

            // the loosely coupled effective-configuration exports (writeEffectiveXML /
            // writeEffectiveConfiguration): startables (NOT auto-activated components - Autofac
            // runs ALL startables, the configuration pipeline among them, before it activates
            // those, so an auto-activated exporter would miss the first effective
            // configuration); they subscribe to the monitor when started and remove their
            // files when the application stops
            builder.RegisterType<EffectiveXMLExporter>()
                .As<EffectiveConfigurationExporter>().As<IStartable>()
                .SingleInstance();
            builder.RegisterType<EffectiveKeyValueExporter>()
                .As<EffectiveConfigurationExporter>().As<IStartable>()
                .SingleInstance();

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
