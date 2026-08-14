using Autofac;
using MadWizard.Desomnia.Daemon.Configuration;
using MadWizard.Desomnia.Daemon.DBus;
using MadWizard.Desomnia.Daemon.DBus.Interface;
using MadWizard.Desomnia.Daemon.DBus.Interface.Adapter;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Power.Source;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Manager.Middleware;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Daemon
{
    internal class PlatformModule : Desomnia.ConfigurableModule
    {
        private static string HostsFilePath => "/etc/hosts";

        // The D-Bus system bus socket is the canonical indicator that logind is reachable.
        private static bool HasSystemDBus() => File.Exists("/run/dbus/system_bus_socket");

        protected override void LoadOnce(ContainerBuilder builder, IConfiguration configuration)
        {
            var config = Bind<DaemonConfig>(configuration);

            RegisterProcessManager(builder, config);
            RegisterPowerManager(builder, config);

            // the network interface manager lives in the persistent container, so it survives a
            // configuration rebuild with its disable intents and took-down bookkeeping intact;
            // created only on first demand, and recorded so a config-less rebuild can re-attach
            builder.RegisterType<IPNetworkInterfaceManager>()
                .As<INetworkInterfaceManager>()
                .SingleInstance()
                .AsSelf();
        }

        private static void RegisterProcessManager(ContainerBuilder builder, DaemonConfig config)
        {
            // Takes the place of the module's own polling fallback (its registration steps aside
            // for any IProcessManager already registered, and platform modules load first): same
            // polling, but a poll that finds nothing new is a single directory read here.
            builder.RegisterType<ProcFSProcessManager>()
                .WithParameter(TypedParameter.From(config.ProcessManager.PollInterval))
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
            builder.RegisterType<LinuxProcess>().As<IProcess>()
                .ConfigurePipeline(pipeline => pipeline.Use(new ProcessExitWatch()))
                .ExternallyOwned();
        }

        private static void RegisterPowerManager(ContainerBuilder builder, DaemonConfig config)
        {
            // Implementing Power-Manager
            if (config.UseDBus && HasSystemDBus())
            {
                builder.RegisterType<DBusManager>()
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();

                builder.RegisterDBusService<ILogin1Manager, Login1Manager>();

                builder.RegisterType<DBusPowerManager>()
                    .WithParameter(TypedParameter.From(config.PowerManager.WatchOperation))
                    .WithParameter(TypedParameter.From(config.PowerManager.WatchMode))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }
            else // fallback
            {
                builder.RegisterType<SysPowerManager>()
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();
            }

            // machine-lifetime power probe, backing the "power" condition of every rebuild
            builder.RegisterType<SysfsPowerSource>()
                .As<IPowerSource>()
                .SingleInstance();
        }

        protected override void Load(ContainerBuilder builder)
        {
            // Options mappings
            builder.RegisterType<HostsManager>()
                .WithParameter(TypedParameter.From(HostsFilePath))
                .AsImplementedInterfaces()
                .SingleInstance();
            builder.RegisterType<IPNeighborCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();

            if (EthtoolOperator.IsInstalled)
            {
                builder.RegisterType<EthtoolOperator>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork();
            }
        }
    }
}
