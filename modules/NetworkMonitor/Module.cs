using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Demand.Detector;
using MadWizard.Desomnia.Network.Demand.Filter;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Impersonation;
using MadWizard.Desomnia.Network.Knocking;
using MadWizard.Desomnia.Network.Knocking.Methods;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Configuration.Xml;

namespace MadWizard.Desomnia.Network
{
    public class Module : Desomnia.ConfigurableModule<ModuleConfig>
    {
        protected override void ConfigureConfigurationSource(ExtendedXmlConfigurationSource source)
        {
            source  .AddBooleanAttribute("must", new() { ["type"] = "Must" })
                    .AddNamelessCollectionElement("NetworkMonitor")
                    .AddNamelessCollectionElement("SharedSecret")
                    .AddNamelessCollectionElement("ServiceFilterRule")
                    .AddNamelessCollectionElement("HostFilterRule")
                    .AddNamelessCollectionElement("HostRangeFilterRule")
                    .AddNamelessCollectionElement("HostRange")
                    .AddNamelessCollectionElement("Host")
                    .AddNamelessCollectionElement("HTTPFilterRuleInfo")
                    .AddNamelessCollectionElement("RequestFilterRule")
                    .AddEnumAttribute("autoDetect")
                    .AddEnumAttribute("advertise")
                    .AddEnumAttribute("protocol")
                    .AddEnumAttribute("wakeType");
        }

        protected override void Load(ContainerBuilder builder)
        {
            if (Config.NetworkMonitor.Count > 0)
            {
                builder.RegisterType<DynamicNetworkObserver>()
                    .WithParameter(TypedParameter.From(Config))
                    .AsImplementedInterfaces()
                    .SingleInstance()
                    .AsSelf();

                // Composites
                builder.RegisterComposite<CompositeRouterDiscovery, IRouterDiscovery>();
                builder.RegisterComposite<CompositeIPAddressDiscovery, IIPAddressDiscovery>();
                builder.RegisterComposite<CompositePhysicalAddressDiscovery, IPhysicalAddressDiscovery>();
                builder.RegisterComposite<CompositeVirtualMachineManager, IVirtualMachineManager>();

                builder.RegisterComposite<CompositePacketFilter, IPacketFilter>();
                builder.RegisterComposite<CompositeKnockFilter,  IKnockFilter>();

                // Knock-Methods
                builder.RegisterType<PlainTextKnockMethod>()
                    .Named<IKnockMethod>("plain")
                    .Named<IKnockDetector>("plain")
                    .AsImplementedInterfaces()
                    .SingleInstance();

                // Network Context
                builder.RegisterType<NetworkContext>()
                    .InstancePerOwned<NetworkContext>()
                    //.SingleInstance()
                    .AsSelf();

                // --- Network Scope ---- //

                // Global Network Filters
                builder.RegisterType<TrafficFilterRequest>()
                    .InstancePerDependency()
                    .AsSelf();
                builder.RegisterType<LocalPacketFilter>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork();

                // Network Services
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
                builder.RegisterType<KnockService>()
                    .AsImplementedInterfaces()
                    .InstancePerNetwork()
                    .AsSelf();

                // Demand Triggers
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

                // --- Network Host Scope ---- //

                // Demand Filters
                builder.RegisterType<DemandRouterFilter>()
                    .As<IDemandPacketFilter>()
                    .InstancePerNetworkHost();
                builder.RegisterType<DemandPacketRuleFilter>()
                    .As<IDemandPacketFilter>()
                    .InstancePerNetworkHost();

                // --- Request Scope ---- //

                builder.RegisterType<DemandRequest>()
                    .InstancePerRequest()
                    .AsSelf();

                // Feed NetworkMonitors dynamically into the SystemMonitor
                builder.RegisterServiceMiddleware<IEnumerable<IInspectable>>(new DynamicNetworkMonitors());
                builder.RegisterServiceMiddleware<IEnumerable<NetworkService>>(new DynamicNetworkServices());
                builder.RegisterServiceMiddleware<IEnumerable<PacketFilterRule>>(new DynamicPacketFilterRules());
            }
        }
    }

    public abstract class NetworkPluginModule : Autofac.Module
    {
        //public virtual void Build(ContainerBuilder builder) { }
    }
}
