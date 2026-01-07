using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using NetTools;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class HostFilterRulesParameter(IPFilterRuleInfo filter) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(IEnumerable<HostFilterRule>), 
        (pi, ctx) =>
        {
            List<HostFilterRule> rules = [];

            foreach (var host in filter.HostFilterRule)
            {
                if (host.IsDynamic)
                {
                    rules.Add(ctx.Resolve<DynamicHostFilterRule>(
                        TypedParameter.From(host.Type),
                        NetworkHostParameter<NetworkHost>.FindBy(host.Name!)));
                }
                else
                {
                    rules.Add(ctx.Resolve<StaticHostFilterRule>(
                        TypedParameter.From(host.Type),
                        TypedParameter.From(host.IPAddresses)));
                }
            }

            foreach (var range in filter.HostRangeFilterRule)
            {
                if (range.IsDynamic)
                {
                    rules.Add(ctx.Resolve<DynamicHostRangeFilterRule>(
                        TypedParameter.From(range.Type),
                        NetworkHostRangeParameter.FindBy(range.Name!)));
                }
                else if (range.AddressRange is IPAddressRange addressRange)
                {
                    rules.Add(ctx.Resolve<StaticHostRangeFilterRule>(
                        TypedParameter.From(range.Type),
                        TypedParameter.From(addressRange)));
                }
            }

            return rules;
        })
    {
        internal static HostFilterRulesParameter From(IPFilterRuleInfo filter) => new(filter);
    }
}
