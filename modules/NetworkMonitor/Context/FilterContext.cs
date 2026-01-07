using Autofac;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Monitor.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using NetTools;
using System.Collections.Concurrent;
using System.Net;

namespace MadWizard.Desomnia.Network.Context
{
    public abstract class FilterContext : Context
    {
        protected ILifetimeScope Scope { get; init; } = null!;

        private readonly bool _needsTCPData;

        private readonly ConcurrentDictionary<NetworkService, TrafficFilterRequest> _dynamicTrafficFilters = [];

        protected FilterContext(ILifetimeScope parent)
        {
            _needsTCPData = parent.ResolveOptional<SystemUsageInspector>() is not null;
        }

        protected void RegisterFilters(ContainerBuilder builder, WatchedHostInfo info)
        {
            RegisterHostFilters(builder, info.HostFilterRule);
            RegisterHostRangeFilters(builder, info.HostRangeFilterRule);
            RegisterServiceFilters(builder, info.ServiceFilterRules);
            RegisterPingFilter(builder, info.PingFilterRule);
        }

        protected void RegisterHostFilters(ContainerBuilder builder, IEnumerable<HostFilterRuleInfo> filters)
        {
            foreach (var filter in filters)
            {
                RegisterHostFilter(builder, filter);
            }
        }

        protected void RegisterHostFilter(ContainerBuilder builder, HostFilterRuleInfo filter)
        {
            if (filter.IsDynamic)
            {
                builder.RegisterType<DynamicHostFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(NetworkHostParameter<NetworkHost>.FindBy(filter.Name!))
                    .As<PacketFilterRule>().As<HostFilterRule>()
                    .SingleInstance();
            }
            else
            {
                builder.RegisterType<StaticHostFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(filter.IPAddresses))
                    .As<PacketFilterRule>().As<HostFilterRule>()
                    .SingleInstance();
            }
        }

        protected void RegisterHostRangeFilters(ContainerBuilder builder, IEnumerable<HostRangeFilterRuleInfo> filters)
        {
            foreach (var filter in filters)
            {
                RegisterHostRangeFilter(builder, filter);
            }
        }

        protected void RegisterHostRangeFilter(ContainerBuilder builder, HostRangeFilterRuleInfo filter)
        {
            if (filter.IsDynamic)
            {
                builder.RegisterType<DynamicHostRangeFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(NetworkHostRangeParameter.FindBy(filter.Name!))
                    .As<PacketFilterRule>().As<HostRangeFilterRule>()
                    .SingleInstance();
            }
            else if (filter.AddressRange is IPAddressRange addressRange)
            {
                builder.RegisterType<StaticHostRangeFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(addressRange))
                    .As<PacketFilterRule>().As<HostRangeFilterRule>()
                    .SingleInstance();
            }
        }

        protected void RegisterForeignHostFilter(ContainerBuilder builder, ForeignHostFilterRuleInfo? filter)
        {
            if (filter != null)
            {
                var register = builder.RegisterType<ForeignHostFilterRule>().As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();
            }
        }

        protected void RegisterServiceFilters(ContainerBuilder builder, IEnumerable<ServiceFilterRuleInfo> filters)
        {
            foreach (var filter in filters)
            {
                RegisterServiceFilter(builder, filter);
            }
        }

        protected void RegisterServiceFilter(ContainerBuilder builder, ServiceFilterRuleInfo filter)
        {
            if (filter.Protocol.HasFlag(IPProtocol.TCP))
            {
                var register = filter switch
                {
                    HTTPFilterRuleInfo => builder.RegisterType<HTTPFilterRule>(), // LATER: add parameters for HTTPRequestFilterRuleInfo

                    ServiceFilterRuleInfo => builder.RegisterType<TCPServiceFilterRule>(),
                };

                register.As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(filter.Port))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();

                RegisterTrafficFilter(builder, new TCPTrafficType(filter.Port, _needsTCPData));
            }
            if (filter.Protocol.HasFlag(IPProtocol.UDP))
            {
                var register = builder.RegisterType<UDPServiceFilterRule>().As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(filter.Port))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();

                RegisterTrafficFilter(builder, new UDPTrafficType(filter.Port));
            }
        }

        protected void RegisterPingFilter(ContainerBuilder builder, PingFilterRuleInfo? filter)
        {
            if (filter is not null)
            {
                RegisterTrafficFilter(builder, new ICMPEchoTrafficType());

                var register = builder.RegisterType<PingFilterRule>().As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();
            }
        }

        protected void AddDynamicTrafficFilters(NetworkHost host, NetworkService service)
        {
            switch (service)
            {
                case TransportNetworkService transport:
                    HashSet<ITrafficType> types = [];

                    if (host.IPv4Addresses.Any())
                        types.Add(new IPv4TrafficType());
                    if (host.IPv6Addresses.Any())
                        types.Add(new IPv6TrafficType());

                    foreach (var srv in transport.Ports)
                    {
                        ITrafficType type = srv.Protocol switch
                        {
                            IPProtocol.TCP => new TCPTrafficType(srv.Port, _needsTCPData),
                            IPProtocol.UDP => new UDPTrafficType(srv.Port),
                            _ => throw new NotImplementedException(),
                        };

                        types.Add(type);
                    }

                    _dynamicTrafficFilters[service] = Scope.UseTrafficType([.. types]);

                    break;
            }
        }

        protected void RemoveDynamicTrafficFilters(NetworkHost host, NetworkService service)
        {
            if (_dynamicTrafficFilters.TryRemove(service, out var request))
            {
                request.Dispose();
            }
        }
    }
}
