using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class HostRangeFilterRuleInfo() : IPAddressRangeInfo
    {
        public string? Name { get; init; }

        public bool IsDynamic => !string.IsNullOrWhiteSpace(Name) && AddressRange == null;

        public FilterRuleType Type { get; set; } = FilterRuleType.MustNot;

        internal HostRangeFilterRuleInfo(string name) : this()
        {
            Name = name;
        }
    }
}
