using Autofac;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Monitor.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using NetTools;
using System.Collections.Concurrent;
using System.Net;

namespace MadWizard.Desomnia.Network.Context
{
    public abstract class FilterContext : Context
    {
        /// <summary> This is to create isolated filters. </summary>
        private string? _tagName;

        private readonly bool _needsTCPData;

        private readonly ConcurrentBag<HostFilterRuleInfo> _dynamicHostFilters = [];

        //public required Lazy<IEnumerable<PacketFilterRule>> Rules { internal get; init; }

        protected FilterContext(ILifetimeScope parent, string? tagName = null)
        {
            _needsTCPData = parent.ResolveOptional<SystemUsageInspector>() is not null;

            _tagName = tagName;
        }

        protected void RegisterTaggedPacketRuleFilter(ContainerBuilder builder)
        {
            builder.RegisterComposite<PacketRuleFilter, IPacketFilter>()
                .WithParameter(new PacketRuleFiltersParameter(_tagName));
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
                    .WithMetadata("tag", _tagName)
                    .AsImplementedInterfaces()
                    .SingleInstance();

                _dynamicHostFilters.Add(filter);
            }
            else
            {
                builder.RegisterType<StaticHostFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(filter.IPAddresses))
                    .As<PacketFilterRule>().As<HostFilterRule>()
                    .WithMetadata("tag", _tagName)
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
                    .WithMetadata("tag", _tagName)
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

        private void RegisterManyHostFilter<I, F>(ContainerBuilder builder, I? filter) where I : EveryHostFilterRuleInfo where F : EveryHostFilterRule
        {
            if (filter != null)
            {
                var register = builder.RegisterType<F>().As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();

                RememberDynamicHostFilters(filter);
            }
        }

        protected void RegisterEveryHostFilter(ContainerBuilder builder, EveryHostFilterRuleInfo? filter) =>
            RegisterManyHostFilter<EveryHostFilterRuleInfo, EveryHostFilterRule>(builder, filter);

        protected void RegisterForeignHostFilter(ContainerBuilder builder, ForeignHostFilterRuleInfo? filter) =>
            RegisterManyHostFilter<ForeignHostFilterRuleInfo, ForeignHostFilterRule>(builder, filter);

        protected void RegisterServiceFilters(ContainerBuilder builder, IEnumerable<ServiceFilterRuleInfo> filters)
        {
            foreach (var filter in filters)
            {
                RegisterServiceFilter(builder, filter);
            }
        }

        protected void RegisterServiceFilter(ContainerBuilder builder, ServiceFilterRuleInfo filter)
        {
            /*
             * A rule's traffic shape is only a capture requirement if the rule can actually wake the host:
             * Must-rules can; MustNot-rules never define needed traffic (their veto is applied in user space).
             */
            bool needsTraffic = filter.Type == FilterRuleType.Must;

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
                    .WithMetadata("tag", _tagName)
                    .SingleInstance()
                    .AsSelf();

                if (needsTraffic)
                    RegisterTrafficFilter(builder, new TCPTrafficType(filter.Port, _needsTCPData));

                RememberDynamicHostFilters(filter);
            }

            if (filter.Protocol.HasFlag(IPProtocol.UDP))
            {
                var register = builder.RegisterType<UDPServiceFilterRule>().As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(filter.Port))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();

                if (needsTraffic)
                    RegisterTrafficFilter(builder, new UDPTrafficType(filter.Port));

                RememberDynamicHostFilters(filter);
            }
        }

        protected void RegisterPingFilter(ContainerBuilder builder, PingFilterRuleInfo? filter)
        {
            if (filter is not null)
            {
                if (filter.Type == FilterRuleType.Must)
                    RegisterTrafficFilter(builder, new ICMPEchoTrafficType());

                var register = builder.RegisterType<PingFilterRule>().As<PacketFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .SingleInstance()
                    .AsSelf();

                RememberDynamicHostFilters(filter);
            }
        }

        protected void RememberDynamicHostFilters(IPFilterRuleInfo rule)
        {
            foreach (var host in rule.HostFilterRule)
            {
                if (host.IsDynamic)
                {
                    _dynamicHostFilters.Add(host);
                }
            }
        }

        public IEnumerable<string> FindMissingDynamicHosts(IEnumerable<NetworkHost> hosts)
        {
            foreach (HostFilterRuleInfo filter in _dynamicHostFilters)
            {
                if (filter.Name is not null && !hosts.Any(host => host.Name == filter.Name))
                {
                    yield return filter.Name;
                }
            }
        }
    }
}
