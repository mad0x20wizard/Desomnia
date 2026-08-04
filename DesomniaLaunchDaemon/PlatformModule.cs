using Autofac;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.LaunchDaemon.Configuration;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Manager;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Manager.Middleware;
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
            // the middleware below cannot resolve the manager – the first processes are created
            // inside the manager's own activation, where a resolve-back would trip Autofac's
            // self-construction guard – so the slot is wired by hand once the manager stands
            var exitWatch = new ProcessExitWatch();

            builder.RegisterType<LibProcProcessManager>()
                .WithParameter(TypedParameter.From(config.ProcessManager?.PollInterval))
                .AsImplementedInterfaces()
                .As<ProcessManager>()
                .SingleInstance()
                .OnActivated(activated => exitWatch.Manager = activated.Instance); // FIXME

            // Externally owned, because the container must not track what it did not get to
            // keep: the manager holds every process' lifetime, and the persistent container
            // would otherwise remember a disposal reference for each of the hundreds a startup
            // materialises. The exit watch rides the registration pipeline, where the concrete
            // process is still unwrapped.
            builder.RegisterType<LibProcProcess>().As<IProcess>()
                .ConfigurePipeline(pipeline => pipeline.Use(exitWatch))
                .ExternallyOwned();

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
