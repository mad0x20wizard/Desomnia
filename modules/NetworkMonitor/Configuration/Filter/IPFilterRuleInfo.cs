using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public abstract class IPFilterRuleInfo
    {
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; init; } = [];

        public FilterRuleType Type { get; set; } = FilterRuleType.MustNot;
    }
}
