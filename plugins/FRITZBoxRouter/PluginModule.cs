using Autofac;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Extensions;
using MadWizard.Desomnia.Network.FRITZ.Actions;
using MadWizard.Desomnia.Network.FRITZ.Configuration;
using MadWizard.Desomnia.Network.FRITZ.Context;
using MadWizard.Desomnia.Network.FRITZ.Discovery;
using MadWizard.Desomnia.Network.FRITZ.Neighborhood;
using NetworkMonitorConfig = MadWizard.Desomnia.Network.FRITZ.Configuration.NetworkMonitorConfig;

namespace MadWizard.Desomnia.Network.FRITZ
{
    /// <summary>
    /// Wires up the FRITZ!Box routers declared in the configuration:
    /// <list type="bullet">
    /// <item>the global <see cref="FRITZBoxOperator"/> scheme handler that carries out <c>fritz://</c>
    /// URL actions (today: LAN port maxspeed), addressed by box name;</item>
    /// <item>a per-network binding that turns each &lt;FRITZBoxRouter&gt; into a
    /// <see cref="FRITZBoxRouter"/> router in that network's segment (VPN clients auto-populated
    /// from the box) and contributes the box' known-host table to the network's address
    /// discovery, so a tracked host's MAC/IP can be resolved from the box.</item>
    /// </list>
    ///
    /// <para>The routers live in the normal host container (<c>NetworkSegment</c>) and are found
    /// there by type or name; clients connect lazily, so a box that is unreachable at startup
    /// only surfaces an error when something actually targets it.</para>
    /// </summary>
    public class PluginModule : ConfigurableModule<ModuleConfig<NetworkMonitorConfig>>
    {
        protected override void Load(ContainerBuilder builder, ModuleConfig<NetworkMonitorConfig> config)
        {
            // A network is in scope if it configures a box, or opted into router autodetect (which enables zero-conf mDNS discovery of boxes it never configured).
            var networks = config.NetworkMonitor.Where(net => net.FRITZBoxRouter.Any() || net.AutoDetect.HasFlag(AutoDiscoveryType.Router)).ToList();

            if (networks.Count > 0)
            {
                builder.RegisterType<FRITZBoxOperator>().As<ActionProvider>()
                    .SingleInstance();

                // Per-network binding: the NetworkMonitor picks up plugin modules whose metadata
                // ordinal matches the network (see NetworkContext), and runs their Load inside
                // that scope. The ordinal correlates this module's view of the configuration
                // with the NetworkMonitor's — both bind the same sections in the same order.
                foreach (var network in networks)
                {
                    builder.RegisterType<NetworkPluginModule>().As<Desomnia.Network.PluginModule>()
                        .WithMetadata<Desomnia.Network.PluginModule.Metadata>(meta => meta.ForNetwork(network))
                        .WithParameter(TypedParameter.From(network))
                        .SingleInstance();
                }
            }
        }
    }

    /// <summary>Registered once per configured network; runs inside that network's scope to create
    /// the FRITZ!Box routers of this segment and join its address-discovery composites.</summary>
    public class NetworkPluginModule(NetworkMonitorConfig config) : Desomnia.Network.PluginModule
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<FRITZBoxRouterContext>()
                .InstancePerDependency()
                .ExternallyOwned()
                .AsSelf();

            // Before the built-in detectors (order 1, 2): when the box is the default gateway,
            // DefaultGatewayDetector then enriches it instead of creating a second router.
            builder.RegisterType<FRITZBoxDetector>().WithPriority(0)
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .WithParameter(new TypedParameter(typeof(IEnumerable<FRITZBoxRouterInfo>), config.FRITZBoxRouter)) // static routers
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<FRITZBoxAddressDetector>()
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }
}
