using Autofac;
using Autofac.Core;
using Autofac.Features.Metadata;
using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class FilterRulesParameter(string? tagName) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(IEnumerable<FilterRule>),
        (pi, ctx) => FindFilterRulesBy(ctx, tagName))
    {
        // Filter the metadata before constructing rules from unrelated parent scopes.
        private static IEnumerable<FilterRule> FindFilterRulesBy(IComponentContext ctx, string? tag) =>
             ctx.Resolve<IEnumerable<Meta<Lazy<FilterRule>>>>()
                .Where(rule => CheckMeta(rule, tag))
                .Select(rule => rule.Value.Value);

        private static bool CheckMeta(Meta<Lazy<FilterRule>> rule, string? tag)
        {
            return rule.Metadata.TryGetValue("tag", out var value) && value is string str && str == tag;
        }
    }
}
