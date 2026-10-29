using Autofac;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Services;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using MadWizard.Desomnia.Network.Watch;
using System.Net;

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
                RegisterService<NetworkService>(builder, info);
                RegisterServiceWatch<NetworkServiceWatch>(builder, info);

                RegisterFilters(builder, info);
            });
        }

        protected void RegisterService<T>(ContainerBuilder builder, WatchedServiceInfo info) where T : NetworkService
        {
            var service = builder.RegisterType<T>().As<NetworkService>()
                .WithParameter(TypedParameter.From(info.Name))
                .WithParameter(TypedParameter.From<IPService>(info))
                .SingleInstance()
                .AsSelf();

            if (info.ServiceName is string name)
                service.WithProperty(nameof(NetworkService.ServiceName), name);
            if (info.InstanceName is string instance)
                service.WithProperty(nameof(NetworkService.InstanceName), instance);

            // DNS-SD advertising attributes carried over from a Sleep Proxy registration.
            service.WithProperty(nameof(NetworkService.Priority), info.Priority);
            service.WithProperty(nameof(NetworkService.Weight), info.Weight);
            service.WithProperty(nameof(NetworkService.Properties), info.Properties);
        }

        protected void RegisterServiceWatch<T>(ContainerBuilder builder, WatchedServiceInfo info) where T : NetworkServiceWatch
        {
            var watch = builder.RegisterType<T>().As<NetworkServiceWatch>()
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
        }

        protected void RegisterFilters(ContainerBuilder builder, WatchedServiceInfo info)
        {
            bool needsPayload = false;

            RegisterHostFilters(builder, info.HostFilterRule);
            RegisterHostRangeFilters(builder, info.HostRangeFilterRule);

            switch (info)
            {
                case WatchedHTTPServiceInfo http when info.Protocol is IPProtocol.TCP:
                    RegisterHTTPRequestFilters(builder, http.RequestFilterRule, ref needsPayload);

                    builder.RegisterType<HTTPFilterContextParser>().As<IFilterContextParser>().SingleInstance();

                    break;
            }

            RegisterServiceTraffic(builder, [info], needsPayload);
        }
    }
}
