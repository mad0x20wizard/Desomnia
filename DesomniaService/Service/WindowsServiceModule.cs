using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Windows
{
    internal class WindowsServiceModule : Desomnia.Module
    {
        protected override void Load(Autofac.ContainerBuilder builder)
        {
            // Attaching Windows Control
            builder.RegisterType<WindowsService>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

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
