using Autofac;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Device.Filter;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
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

        protected FilterContext(ILifetimeScope parent, string? tagName = null)
        {
            _needsTCPData = parent.ResolveOptional<SystemUsageInspector>() is not null;

            _tagName = tagName;
        }

        protected void RegisterTaggedPacketRuleFilter(ContainerBuilder builder)
        {
            builder.RegisterComposite<RuleFilter, IFilter>()
                .WithParameter(new FilterRulesParameter(_tagName));
        }

        protected void RegisterFilters(ContainerBuilder builder, IFilterInfo info)
        {
            RegisterHostFilters(builder, info.HostFilterRule);
            RegisterHostRangeFilters(builder, info.HostRangeFilterRule);
            RegisterEveryHostFilter(builder, info.EveryHostFilterRule);
            RegisterForeignHostFilter(builder, info.ForeignHostFilterRule);
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
                    .As<FilterRule>().As<HostFilterRule>()
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
                    .As<FilterRule>().As<HostFilterRule>()
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
                    .As<FilterRule>().As<HostRangeFilterRule>()
                    .WithMetadata("tag", _tagName)
                    .SingleInstance();
            }
            else if (filter.AddressRange is IPAddressRange addressRange)
            {
                builder.RegisterType<StaticHostRangeFilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(addressRange))
                    .As<FilterRule>().As<HostRangeFilterRule>()
                    .WithMetadata("tag", _tagName)
                    .SingleInstance();
            }
        }

        private void RegisterManyHostFilter<I, F>(ContainerBuilder builder, I? filter) where I : EveryHostFilterRuleInfo where F : EveryHostFilterRule
        {
            if (filter != null)
            {
                var register = builder.RegisterType<F>().As<FilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .WithMetadata("tag", _tagName)
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
            foreach (var service in filter)
            {
                builder.RegisterType<ServiceFilterRule>().As<FilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From(service))
                    .WithProperty(HostFilterRulesParameter.From(filter))
                    .WithMetadata("tag", _tagName)
                    .SingleInstance()
                    .AsSelf();

                if (filter.Type is FilterRuleType.Must or FilterRuleType.May)
                {
                    RegisterServiceTraffic(builder, [service]);
                }
            }

            RememberDynamicHostFilters(filter);
        }

        /// <summary>Capture selection belongs to the service, independently of its admission rules.</summary>
        protected void RegisterServiceTraffic(ContainerBuilder builder, IEnumerable<IPService> ports, bool needsPayload = false)
        {
            foreach (var port in ports.Distinct())
            {
                if (port.Protocol.HasFlag(IPProtocol.TCP))
                    RegisterTrafficFilter(builder, new TCPTrafficType(port.Port, _needsTCPData || needsPayload));
                if (port.Protocol.HasFlag(IPProtocol.UDP))
                    RegisterTrafficFilter(builder, new UDPTrafficType(port.Port));
            }
        }

        protected void RegisterHTTPRequestFilters(ContainerBuilder builder, IEnumerable<HTTPRequestFilterRuleInfo> filters, ref bool needsPayload)
        {
            foreach (var filter in filters)
            {
                builder.RegisterType<HTTPRequestFilterRule>().As<FilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(TypedParameter.From<HTTPRequestPattern>(filter))
                    .WithParameter(nameof(HTTPRequestFilterRule.Rules), Array.Empty<HTTPRequestFilterRule>()) // TODO: Implement nested rules recursively
                    .WithMetadata("tag", _tagName)
                    .SingleInstance();

                needsPayload |= true;
            }
        }

        protected void RegisterPingFilter(ContainerBuilder builder, PingFilterRuleInfo? filter)
        {
            if (filter is not null)
            {
                if (filter.Type is FilterRuleType.Must or FilterRuleType.May)
                    RegisterTrafficFilter(builder, new ICMPEchoTrafficType());

                builder.RegisterType<PingFilterRule>().As<FilterRule>()
                    .WithParameter(TypedParameter.From(filter.Type))
                    .WithParameter(HostFilterRulesParameter.From(filter))
                    .WithMetadata("tag", _tagName)
                    .SingleInstance()
                    .AsSelf();

                RememberDynamicHostFilters(filter);
            }
        }

        #region Dynamic FilterRule Host resolution
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
            foreach (string name in _dynamicHostFilters.Where(f => f.IsDynamic).Select(f => f.Name).OfType<string>().Distinct())
            {
                if (!hosts.Any(host => host.Name == name))
                {
                    yield return name;
                }
            }
        }

        protected internal virtual void ResolveFilterRules()
        {
            Scope?.Resolve<IEnumerable<FilterRule>>();
        }
        #endregion
    }
}
