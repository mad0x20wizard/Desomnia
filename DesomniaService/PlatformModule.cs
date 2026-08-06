using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.Environments;
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
using MadWizard.Desomnia.Service.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Service
{
    internal class PlatformModule : Desomnia.ConfigurableModule
    {
        private static string HostsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        protected override void LoadOnce(ContainerBuilder builder, IConfiguration configuration)
        {
            var config = Bind<ServiceConfig>(configuration);

            builder.RegisterType<PowerManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<PowerSourceCondition>()
                .Named<IEnvironmentCondition>("power");

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
            // process is still unwrapped.
            builder.RegisterType<Win32Process>().As<IProcess>()
                .ConfigurePipeline(pipeline => pipeline.Use(new ProcessExitWatch()))
                .ExternallyOwned();

            // The precise traffic meter is a plugin to the trace session: registered, it rides
            // along and books into the decoration that wraps every process; absent, the processes
            // answer with the passive IO-counter approximation. Future per-process metrics follow
            // the same pattern – a registration and a decoration, not a manager or process change.
            if (config.ProcessManager.WatchTraffic == ProcessTrafficWatch.Active)
            {
                builder.RegisterDecorator<ProcessTrafficLayer, IProcess>();

                builder.RegisterType<ProcessTrafficMetric>()
                    .As<ITraceEventMetric>()
                    .SingleInstance();
            }

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

            // the display manager lives in the persistent container, so it survives a
            // configuration rebuild (the same problem as macOS, ahead of Windows soft-disconnect);
            // created only on first demand, and recorded so a config-less rebuild can re-attach
            builder.RegisterType<WindowsDisplayManager>()
                .As<IDisplayManager>()
                .SingleInstance()
                .AsSelf();
        }

        protected override void Load(ContainerBuilder builder)
        {
            // Address mappings
            builder.RegisterType<HostsManager>()
                .WithParameter(TypedParameter.From(HostsFilePath))
                .AsImplementedInterfaces()
                .SingleInstance();
            builder.RegisterType<NetshNeighborCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();

            // Wake-on-LAN adapter
            builder.RegisterComposite<WindowsWakeOnLANManager, IWakeOnLANManager>();
            {
                builder.RegisterType<CIMNetAdapterPowerManagement>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork();
                builder.RegisterType<CIMDeviceWake>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork();
            }

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
                .As<ActionProvider>();

            builder.RegisterType<TerminalServicesBroadcaster>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(TerminalServicesManager))))
                .AsImplementedInterfaces()
                .SingleInstance()
                .As<ActionProvider>();
        }
    }
}
