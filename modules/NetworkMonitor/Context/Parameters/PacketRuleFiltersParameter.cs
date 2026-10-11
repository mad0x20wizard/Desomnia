using Autofac;
using Autofac.Core;
using Autofac.Features.Metadata;
using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class PacketRuleFiltersParameter(string? tagName) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(IEnumerable<PacketFilterRule>),
        (pi, ctx) => FindFiltersBy(ctx, tagName))
    {
        // Filter the metadata before constructing rules from unrelated parent scopes.
        private static IEnumerable<PacketFilterRule> FindFiltersBy(IComponentContext ctx, string? tag) =>
             ctx.Resolve<IEnumerable<Meta<Lazy<PacketFilterRule>>>>()
                .Where(rule => CheckMeta(rule, tag))
                .Select(rule => rule.Value.Value);

        private static bool CheckMeta(Meta<Lazy<PacketFilterRule>> rule, string? tag)
        {
            return rule.Metadata.TryGetValue("tag", out var value) && value is string str && str == tag;
        }
    }
}
