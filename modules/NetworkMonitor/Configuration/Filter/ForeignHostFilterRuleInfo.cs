using MadWizard.Desomnia.Network.Configuration.Knocking;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class ForeignHostFilterRuleInfo : IPFilterRuleInfo
    {
        public IList<DynamicHostRangeInfo> DynamicHostRange
        {
            get => field;

            init
            {
                foreach (var dynamic in (field = value))
                {
                    if (!HostRangeFilterRule.Any(rule => rule.IsDynamic && rule.Name == dynamic.Name))
                    {
                        HostRangeFilterRule.Add(new(dynamic.Name));
                    }
                }
            }
        } = [];
    }
}
