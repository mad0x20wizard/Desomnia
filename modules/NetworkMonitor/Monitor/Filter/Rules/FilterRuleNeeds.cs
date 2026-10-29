using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Monitor.Filter.Rules
{
    public struct FilterRuleNeeds
    {
        public int  Must            { get; set; }
        public bool May             { get; set; }

        public bool IPTraffic       { get; }
        public bool ServicePayload  { get; }

        public FilterRuleNeeds(IEnumerable<FilterRule> rules)
        {
            May             = rules.Any(rule => rule.Type == FilterRuleType.May);
            Must            = rules.Count(rule => rule.Type == FilterRuleType.Must);

            IPTraffic       = rules.Any(rule => rule.Needs<IIPFilterContext>());
            ServicePayload  = rules.Any(rule => rule.Needs<IIPServicePayloadFilterContext>());
        }
    }
}
