using Autofac;
using MadWizard.Desomnia.Network.HyperV.Events;
using MadWizard.Desomnia.Network.HyperV.Manager;

namespace MadWizard.Desomnia.Network.HyperV
{
    public class PluginModule : Desomnia.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<HyperVManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<HyperVVirtualMachine>()
                .AsImplementedInterfaces()
                .AsSelf();
            builder.RegisterType<HyperVJob>()
                .AsImplementedInterfaces()
                .AsSelf();

            builder.RegisterType<HyperVEventLogWatcher>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            // overwrite pcap device selection for NetworkDevice
            builder.ComponentRegistryBuilder.Registered += (sender, args) =>
            {
                if (args.ComponentRegistration.IsLimitedTo<NetworkDevice>())
                    args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                        pipeline.Use(new HyperVDeviceSelector());
            };

        }
    }
}
