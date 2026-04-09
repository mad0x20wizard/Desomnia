using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.NetworkSession.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Process.Manager;
using MadWizard.Desomnia.Service.Actions;
using MadWizard.Desomnia.Service.Configuration;

namespace MadWizard.Desomnia.Service
{
    internal class PlatformModule : Desomnia.ConfigurableModule<ServiceConfig>
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Implementing Platform-Managers
            builder.RegisterType<PowerManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            if (Config.ProcessMonitor?.PollInterval is not TimeSpan)
            {
                builder.RegisterType<TraceEventProcessManager>()
                    .AsImplementedInterfaces()
                    .As<ProcessManager>()
                    .SingleInstance()
                    .AsSelf();
            }

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

            RegisterActions(builder);
        }

        private static void RegisterActions(ContainerBuilder builder)
        {
            builder.RegisterType<CommandExecutor>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .As<Actor>();

            builder.RegisterType<TerminalServicesBroadcaster>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .As<Actor>();
        }
    }
}
