using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.NetworkSession.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Process.Manager;
using MadWizard.Desomnia.Service.Actions;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service
{
    internal class PlatformModule : Desomnia.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Implementing Platform-Managers
            builder.RegisterType<PowerManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<ProcessManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<WindowsNeighborCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();

            // Implementing Network-Session-Managers
            builder.RegisterType<CIMNetworkSessionManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<CIMNetworkShareManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<CIMNetworkFileManager>()
                .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<TerminalServicesManager>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(WindowsService))))
                .PropertiesAutowired()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            RegisterActions(builder);
        }

        private static void RegisterActions(ContainerBuilder builder)
        {
            builder.RegisterType<CommandExecutor>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .As<Actor>();
        }
    }
}
