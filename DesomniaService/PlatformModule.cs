using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Bridges;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.NetworkSession.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Manager.Metrics;
using MadWizard.Desomnia.Processes.Middleware;
using MadWizard.Desomnia.Service.Actions;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Service
{
    internal class PlatformModule : Desomnia.ConfigurableModule
    {
        private static string HostsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        protected override void LoadOnce(ContainerBuilder builder, IConfiguration configuration)
        {
            RegisterPowerManager(builder);
            RegisterProcessManager(builder);
            RegisterNetworkInterfaceManager(builder);
            RegisterDisplayManager(builder);
        }

        #region Persistent Managers
        private static void RegisterPowerManager(ContainerBuilder builder)
        {
            builder.RegisterType<PowerManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }

        private static void RegisterProcessManager(ContainerBuilder builder)
        {
            builder.RegisterType<TraceEventProcessManager>()
                .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
                .AsImplementedInterfaces()
                .As<IProcessMetricSupport>()
                .As<ProcessManager>()
                .SingleInstance()
                .AsSelf();

            // Externally owned, because the container must not track what it did not get to
            // keep: the manager holds every process' lifetime, and the persistent container
            // would otherwise remember a disposal reference for each of the hundreds a startup
            // materialises. The exit watch rides the registration pipeline, where the concrete
            // process is still unwrapped (the process module hooks its parent resolver onto
            // the same pipeline, for every platform's registration at once).
            builder.RegisterType<Win32Process>().As<IProcess>()
                .ConfigurePipeline(pipeline => pipeline.Use(new ProcessExitWatch()))
                .ExternallyOwned();

            // The precise traffic meter, always in place and never idling: the decoration
            // subscribes itself to the listener on its first NetworkData sample, and the
            // listener's kernel session exists only while subscribed accounts do. There is no
            // passive fallback to configure against anymore – the IO counters' Other bucket
            // measured device-control chatter, not the network.
            builder.RegisterDecorator<ProcessTrafficAccount, IProcess>();

            builder.RegisterType<TraceEventTrafficListener>()
                .As<IProcessTrafficMeter>()
                .SingleInstance()
                .AsSelf();
        }

        private static void RegisterNetworkInterfaceManager(ContainerBuilder builder)
        {
            // takes over from the platform-neutral matcher the NetworkMonitor module registers
            // with PreserveExistingDefaults (its LoadOnce runs after this one), so conditions
            // and the application match interfaces by their display name and SSID as well
            builder.RegisterType<WindowsInterfaceMatcher>().As<InterfaceMatcher>()
                .InstancePerDependency();

            // like the display manager: persistent, created only on first demand, recorded
            // so a config-less rebuild can re-attach — and on this platform additionally the
            // keeper of adapters it disabled, which Windows drops from the BCL enumeration
            builder.RegisterType<CIMNetworkInterfaceManager>()
                .As<INetworkInterfaceManager>()
                .SingleInstance()
                .AsSelf();
        }

        private static void RegisterDisplayManager(ContainerBuilder builder)
        {
            // the display manager lives in the persistent container, so it survives a
            // configuration rebuild (the same problem as macOS, ahead of Windows soft-disconnect);
            // created only on first demand, and recorded so a config-less rebuild can re-attach
            builder.RegisterType<WindowsDisplayManager>()
                .As<IDisplayManager>()
                .SingleInstance()
                .AsSelf();
        }
        #endregion

        protected override void Load(ContainerBuilder builder)
        {
            RegisterActions(builder);

            RegisterNetworkManager(builder);
            RegisterNetworkSessionManager(builder);

            // global host->IP mappings
            builder.RegisterType<HostsManager>()
                .WithParameter(TypedParameter.From(HostsFilePath))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private static void RegisterNetworkManager(ContainerBuilder builder)
        {
            builder.RegisterType<NetshNeighborCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();

            builder.RegisterComposite<WindowsWakeOnLANManager, IWakeOnLANManager>();
            {
                builder.RegisterType<CIMNetAdapterPowerManagement>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork();
                builder.RegisterType<CIMDeviceWake>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork();
            }
        }

        private static void RegisterNetworkSessionManager(ContainerBuilder builder)
        {
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
        }

        private static void RegisterActions(ContainerBuilder builder)
        {
            builder.RegisterType<CommandExecutor>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .As<ActionProvider>();

            builder.RegisterType<TerminalServicesBroadcaster>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(TerminalServicesManager))))
                .AsImplementedInterfaces()
                .SingleInstance()
                .As<ActionProvider>();
        }
    }
}
