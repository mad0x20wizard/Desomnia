using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Monitor.Filter.Rules;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class RuleFilter(IEnumerable<FilterRule> rules) : IFilter
    {
        public FilterRuleNeeds Needs { get; set; } = new(rules); // implement middleware to override payload need for HTTP Auth

        public IEnumerable<FilterRule> Rules => rules;

        public virtual bool ShouldFilter(IFilterContext context)
        {
            return !FilterRule.MatchesRules(Rules, context, Needs);
        }
    }
}
