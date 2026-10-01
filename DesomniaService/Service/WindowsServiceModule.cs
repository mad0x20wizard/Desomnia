using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Daemon.Configuration;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using MadWizard.Desomnia.Session.Middleware;

namespace MadWizard.Desomnia.Service
{
    internal class WindowsServiceModule : Desomnia.ConfigurableModule<ServiceMonitorConfig>
    {
        protected override void LoadOnce(ContainerBuilder builder)
        {
            // The Windows service IS the persistent host's lifetime: it maps the SCM to the host's
            // application lifetime, so a configuration rebuild (which only cycles the inner host)
            // never reports the service stopped. AsImplementedInterfaces covers IHostLifetime —
            // registered after the framework's default console lifetime, so it wins; AsSelf so the
            // OnlyIf gates and the power/session consumers resolve it (AsSelf is bridged into every
            // inner container, IHostLifetime is not, so the inner hosts keep their passive lifetime).
            //
            // ExternallyOwned, because disposing this instance is NOT the container's business:
            // ServiceBase.Run disposes it in its own finally the moment the SCM dispatcher returns,
            // outside Autofac and beyond any registration order. The container's teardown no longer
            // needs to beat that race: the OS-state restore runs in the persistent host's stop
            // phase (IAsyncStoppable via the ShutdownCoordinator), inside the SCM stop window.
            builder.RegisterType<WindowsService>().AsSelf()
                .AsImplementedInterfaces()
                .ExternallyOwned()
                .SingleInstance();
        }

        protected override void Load(ContainerBuilder builder, ServiceMonitorConfig config)
        {
            RegisterSessionManager(builder, config.SessionMonitor);
        }

        private static void RegisterSessionManager(ContainerBuilder builder, SessionMonitorConfig? config)
        {
            builder.RegisterType<TerminalServicesManager>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(WindowsService))))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<TerminalServicesSession>()
                .As<ISession>() // NOT .As<IProcessManager>() !!!
                .AsSelf();

            if (config?.WatchRemote ?? false)
            {
                // Add RDP port to SleepProxyRegistration
                builder.ComponentRegistryBuilder.Registered += (sender, args) =>
                {
                    if (args.ComponentRegistration.IsLimitedTo<SleepProxyRegistration>())
                        args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                            pipeline.Use(new RDPSleepProxyRegistration());
                };
            }
        }
    }
}
