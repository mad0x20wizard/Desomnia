using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class HostFilterRuleInfo : IPAddressInfo
    {
        public string? Name { get; init; }

        public bool IsDynamic => !string.IsNullOrWhiteSpace(Name) && !IPAddresses.Any();

        public FilterRuleType Type { get; set; } = FilterRuleType.MustNot;
    }
}
