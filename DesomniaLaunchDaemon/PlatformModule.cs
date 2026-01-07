using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Manager;

namespace MadWizard.Desomnia.LaunchDaemon
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

            // Implementing Network-Managers
            builder.RegisterType<MacOSNeighborCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();
        }
    }
}
