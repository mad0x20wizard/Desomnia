using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Bridges;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Context.Bridges;
using MadWizard.Desomnia.Network.Datagram;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Demand.Detector;
using MadWizard.Desomnia.Network.Environments;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Handoff;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Methods;
using MadWizard.Desomnia.Network.Logging;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Manager.Guard;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Reachability;
using MadWizard.Desomnia.Power.Guard;
using NLog;
using NLog.Config;
using System.ComponentModel;

namespace MadWizard.Desomnia.Network
{
    public class Module : ConfigurableModule<ModuleConfig<NetworkMonitorConfig>>
    {
        protected override void ConfigureLogging(ISetupExtensionsBuilder builder)
        {
            builder.RegisterLayoutRenderer<NetworkHostLayoutRenderer>();
            builder.RegisterLayoutRenderer<NetworkLayoutRenderer>(); 
            builder.RegisterLayoutRenderer<NetworkRealmLayoutRenderer>();
        }

        protected override void LoadOnce(ContainerBuilder builder)
        {
            // The platform-neutral interface matcher, registered as a fallback: the platform hosts
            // load BEFORE this module (see the RegisterModule order in their Program), so a host
            // that brings its own matcher has already claimed the default, and
            // PreserveExistingDefaults leaves it that claim. Persistent, because the environment
            // conditions below consume it before any application container exists; the application
            // resolves it through the bridge, a fresh instance per request.
            builder.RegisterType<InterfaceMatcher>()
                .PreserveExistingDefaults()
                .InstancePerDependency()
                .AsSelf();

            // the environment conditions of this module, keyed by their attribute; a platform
            // host may take these over the same way (its LoadOnce ran first), and the injected
            // matcher is the default one - i.e. the platform's, where one exists
            builder.RegisterType<NetworkCondition>()
                .Named<IEnvironmentCondition>("network")
                .PreserveExistingDefaults();
            builder.RegisterType<InterfaceCondition>()
                .Named<IEnvironmentCondition>("interface")
                .PreserveExistingDefaults();
            builder.RegisterType<SSIDCondition>()
                .Named<IEnvironmentCondition>("ssid")
                .PreserveExistingDefaults();

            // no "ssid" here - there is no wireless information to be had without the
            // platform underneath it, so only a platform host can register that condition
        }

        protected override void Load(ContainerBuilder builder, ModuleConfig<NetworkMonitorConfig> config)
        {
            // Registered whether or not the configuration mentions networks: the observer
            // is the sole desired-state arbiter for interface blocks, and a configuration
            // that dropped every monitor still needs one round to release the intents a
            // predecessor asserted (it never CREATES the manager for that — see its
            // CreationTracker gate). Gated on the platform manager, which every platform
            // host registers persistently.
            builder.RegisterType<DynamicNetworkObserver>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(INetworkInterfaceManager))))
                .WithParameter(new TypedParameter(typeof(IEnumerable<NetworkMonitorConfig>), config.NetworkMonitor))
                .WithParameter(new TypedParameter(typeof(IEnumerable<NetworkInterfaceBlockInfo>), config.NetworkInterfaceBlock))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            // Composites //

            builder.RegisterComposite<CompositeVirtualMachineManager, IVirtualMachineManager>();

            // Knock-Methods
            builder.RegisterType<PlainTextKnockMethod>()
                .Named<IKnockMethod>("plain")
                .Named<IKnockDetector>("plain")
                .AsImplementedInterfaces()
                .SingleInstance();

            // Network Context //

            builder.RegisterType<NetworkContext>()
                .InstancePerOwned<NetworkContext>()
                .AsSelf();

            // Global Network Filters //

            builder.RegisterType<TrafficFilterRequest>()
                .InstancePerDependency()
                .AsSelf();
            builder.RegisterType<LocalPacketFilter>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();

            // Packet Filters //

            builder.RegisterType<RouterFilter>()
                .As<IPacketFilter>()
                .InstancePerNetwork();
            builder.RegisterType<PacketRuleFilter>()
                .As<IPacketFilter>()
                .InstancePerMatchingLifetimeScope(
                    MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag,
                    MatchingScopeLifetimeTags.NetworkServiceLifetimeScopeTag);

            builder.RegisterComposite<CompositePacketFilter, IPacketFilter>();

            // Network Services //

            // The application-wide keeper of OS-level UDP sockets: DatagramServices registered
            // with SocketMetadata are linked to it at construction (see DefaultDatagramSocket),
            // and their socket closes again with its last user.
            builder.RegisterType<UDPSocketService>()
                .SingleInstance()
                .AsSelf();

            // Safeguard around the platform neighbour cache: track installed mappings, suppress
            // deletes of entries already removed, and purge any leftovers when the network
            // scope is disposed.
            builder.RegisterDecorator<GuardedLocalAddressMapping, ILocalAddressMapping>();

            builder.RegisterType<AddressMappingService>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();
            builder.RegisterType<ReachabilityService>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();
            builder.RegisterType<ReachabilityCache>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();
            builder.RegisterType<DemandService>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();
            builder.RegisterType<HandoffService>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();
            builder.RegisterType<KnockService>()
                .AsImplementedInterfaces()
                .InstancePerNetwork()
                .AsSelf();


            // Demand Triggers //

            builder.RegisterType<DemandByIP>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();
            builder.RegisterType<DemandByARP>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();
            builder.RegisterType<DemandByNDP>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();
            builder.RegisterType<DemandByWOL>()
                .AsImplementedInterfaces()
                .InstancePerNetwork();

            // --- Request Scope ---- //

            builder.RegisterType<DemandRequest>()
                .InstancePerRequest()
                .AsSelf();

            // Make NetworkMonitors dynamically available. NOTE: the IInspectable
            // bridge is gone — inspection membership is handed over explicitly by
            // the NetworkInspectionBridge at MonitoringStarted/Stopped (§7.2); the
            // power-transition and monitor-enumeration bridges stay (GuardedPowerManager,
            // FRITZBoxOperator).
            builder.RegisterServiceMiddleware<IEnumerable<IPowerTransitionGuard>>(new DynamicNetworkMonitors<IPowerTransitionGuard>());
            builder.RegisterServiceMiddleware<IEnumerable<NetworkMonitor>>(new DynamicNetworkMonitors<NetworkMonitor>());

            builder.RegisterType<SystemMonitorBridge>()
                .OnlyIf(reg => reg.IsRegistered(new TypedService(typeof(INetworkInterfaceManager)))) // it injects the observer, gated the same way
                .As<IStartable>()
                .SingleInstance();
        }
    }

    public abstract class PluginModule : Autofac.Module
    {
        public class Metadata
        {
            /// <summary>
            /// The <see cref="Configuration.NetworkMonitorConfig.Ordinal"/> of the one network
            /// this module belongs to — null for a module that applies to every network. The
            /// ordinal (not the name) is the correlation identity: the plugin binds its own
            /// view of the same configuration, and a network may not be named at all.
            /// </summary>
            [DefaultValue(null)]
            public int? Network { get; set; }
        }
    }
}
