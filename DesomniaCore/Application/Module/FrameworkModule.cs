using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Application.Lifetime;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Environments.Export;
using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia.Application.Module
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
    internal class FrameworkModule : Autofac.Module
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

            // the loosely coupled effective-configuration exports (outputEffectiveXML /
            // outputEffectiveConfiguration); they react to the monitor's change event and
            // remove their files when the application stops
            builder.RegisterType<EffectiveXMLExporter>()
                .As<EffectiveConfigurationExporter>()
                .SingleInstance().AutoActivate();
            builder.RegisterType<EffectiveKeyValueExporter>()
                .As<EffectiveConfigurationExporter>()
                .SingleInstance().AutoActivate();

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
