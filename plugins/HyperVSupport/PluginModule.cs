using Autofac;
using MadWizard.Desomnia.Network.HyperV.Configuration;
using MadWizard.Desomnia.Network.HyperV.Events;
using MadWizard.Desomnia.Network.HyperV.Manager;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Network.HyperV
{
    public class PluginModule : Desomnia.ConfigurableModule
    {
        private VirtualTraffic _watchTraffic = VirtualTraffic.Internal | VirtualTraffic.External;

        protected override void LoadOnce(ContainerBuilder builder, IConfiguration configuration)
        {
            var config = Bind<HyperVConfig>(configuration);

            _watchTraffic = config.HyperV.WatchVirtualTraffic;
        }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<HyperVManager>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            builder.RegisterType<HyperVM>()
                .As<IVirtualMachine>()
                .AsSelf();
            builder.RegisterType<HyperVSwitch>()
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
                        pipeline.Use(new HyperVDeviceSwitcher{ WatchVirtualTraffic = _watchTraffic });
            };
        }
    }
}
