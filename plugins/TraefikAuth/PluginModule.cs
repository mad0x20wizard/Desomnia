using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Extensions;
using MadWizard.Desomnia.Network.Traefik.Filter;
using NetworkMonitorConfig = MadWizard.Desomnia.Network.Traefik.Configuration.NetworkMonitorConfig;

namespace MadWizard.Desomnia.Network.Traefik
{
    public class PluginModule : Desomnia.ConfigurableModule<ModuleConfig<NetworkMonitorConfig>>
    {
        protected override void Load(ContainerBuilder builder, ModuleConfig<NetworkMonitorConfig> config)
        {
            foreach (var network in config.NetworkMonitor)
            {
                builder.RegisterType<NetworkPluginModule>().As<Network.PluginModule>()
                    .WithMetadata<Network.PluginModule.Metadata>(meta => meta.ForNetwork(network))
                    .WithParameter(TypedParameter.From(network))
                    .SingleInstance();
            }

            builder.RegisterComposite<CompositeTraefikRequestFilter, ITraefikRequestFilter>();
        }
    }

    public class NetworkPluginModule : Desomnia.Network.PluginModule
    {
        public required NetworkMonitorConfig Config { private get; init; }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<TraefikAuthListener>()
                .As<INetworkService>()
                .SingleInstance();
        }
    }
}
