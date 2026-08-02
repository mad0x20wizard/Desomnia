using Autofac;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.LaunchDaemon.Configuration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Processes.Manager;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.LaunchDaemon
{
    internal class PlatformModule : Desomnia.ConfigurableModule
    {
        protected override void LoadOnce(ContainerBuilder builder, IConfiguration configuration)
        {
            var config = Bind<LaunchDaemonConfig>(configuration);

            // the display manager lives in the persistent container, so it survives a
            // configuration rebuild with its soft-disconnect holds and CG display ids intact;
            // created only on first demand, and recorded so a config-less rebuild can re-attach
            builder.RegisterType<MacOSDisplayManager>()
                .As<IDisplayManager>()
                .SingleInstance()
                .AsSelf();

            // Takes the place of the module's own polling fallback (its registration steps aside
            // for any IProcessManager already registered, and platform modules load first): same
            // polling, but a poll that finds nothing new costs a single syscall here.
            builder.RegisterType<LibProcProcessManager>()
                .WithParameter(TypedParameter.From(config.ProcessManager?.PollInterval))
                .AsImplementedInterfaces()
                .As<ProcessManager>()
                .SingleInstance();

            // the interface manager is persistent for the same reason: a standing disable
            // intent (and what it took away) must survive a configuration rebuild, and only
            // an instance that outlives every rebuild can restore it on process exit
            builder.RegisterType<NetToolsInterfaceManager>()
                .As<INetworkInterfaceManager>()
                .SingleInstance()
                .AsSelf();

            // Implementing Platform-Managers. The power manager is persistent (its IOKit assertions
            // and sleep/wake registration must outlive a configuration rebuild), which lets it serve
            // as the IPowerSourceProbe backing the "power" condition of every rebuild as well —
            // both notification sources on its one run loop.
            builder.RegisterType<IOKitPowerManager>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<PowerSourceCondition>()
                .Named<IEnvironmentCondition>("power");
        }

        protected override void Load(ContainerBuilder builder)
        {

            // Implementing Network-Managers
            builder.RegisterType<ArpNdpCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();
        }
    }
}
