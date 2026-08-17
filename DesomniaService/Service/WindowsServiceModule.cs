using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Windows
{
    internal class WindowsServiceModule : Desomnia.Module
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

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<TerminalServicesManager>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(WindowsService))))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<TerminalServicesSession>()
                .As<ISession>().As<IDisposable>() // NOT .As<IProcessManager>() !!!
                .AsSelf();
        }
    }
}
