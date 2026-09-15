using Autofac;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration.Services;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using MadWizard.Desomnia.Network.Watch;

namespace MadWizard.Desomnia.Network.Context
{
    public class NetworkServiceContext : FilterContext
    {
        public NetworkService       Service     => field ??= Scope.Resolve<NetworkService>();
        public NetworkServiceWatch  Watch       => field ??= Scope.Resolve<NetworkServiceWatch>();

        public ServiceOptions Options { get; init; }

        protected NetworkServiceContext(ILifetimeScope parent, ServiceOptions options = default) : base(parent) { Options = options; }

        public NetworkServiceContext(ILifetimeScope parent, WatchedServiceInfo info, ServiceOptions options = default) : this(parent, options)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkServiceLifetimeScopeTag, builder =>
            {
                var service = builder.RegisterType<TransportNetworkService>().As<NetworkService>()
                    .WithParameter(TypedParameter.From(info.Name))
                    .WithParameter(TypedParameter.From(info.IPPort))
                    .SingleInstance()
                    .AsSelf();

                // Named property injection: the service has several settable properties, and a
                // TypedParameter would indiscriminately fill same-typed ones.
                if (info.ServiceName is string name)
                {
                    service.WithProperty(nameof(TransportNetworkService.ServiceName), name);
                }

                if (info.InstanceName is string instance)
                {
                    service.WithProperty(nameof(TransportNetworkService.InstanceName), instance);
                }

                // DNS-SD advertising attributes carried over from a Sleep Proxy registration.
                service.WithProperty(nameof(TransportNetworkService.Priority), info.Priority);
                service.WithProperty(nameof(TransportNetworkService.Weight), info.Weight);

                if (info.Properties.Count > 0)
                {
                    service.WithProperty(nameof(TransportNetworkService.Properties), info.Properties);
                }

                var watch = builder.RegisterType<ServiceFilterWatch>().As<NetworkServiceWatch>()
                    .WithProperty(TypedParameter.From(info.MakeAdvertiseOptions()))
                    .WithProperty(TypedParameter.From(info.MakeKnockOptions()))
                    .WithProperty(TypedParameter.From(info.MinTraffic))
                    .SingleInstance()
                    .AsSelf();

                watch.OnActivated(args =>
                {
                    args.Instance.ShouldHandoffToSleepProxy = info.Handoff;

                    ((IEventSystem)args.Instance)[nameof(NetworkServiceWatch.Usage)].AddAction(info.OnUsage);
                    ((IEventSystem)args.Instance)[nameof(NetworkServiceWatch.Demand)].AddAction(info.OnDemand);
                    ((IEventSystem)args.Instance)[nameof(NetworkServiceWatch.Idle)].AddAction(info.OnIdle);
                });

                RegisterServiceFilter(builder, info);
            });
        }

        protected void RegisterService(ContainerBuilder builder, TransportNetworkService service)
        {
            builder.RegisterInstance(service).As<NetworkService>();

            RegisterServiceFilter(builder, service);
        }
    }
}
