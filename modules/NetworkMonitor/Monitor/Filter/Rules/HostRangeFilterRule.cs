using MadWizard.Desomnia.Network.Monitor.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class HostRangeFilterRule : HostFilterRule { }

    public class StaticHostRangeFilterRule : HostRangeFilterRule
    {
        public required IPAddressRange Range { get; init; }

        public override bool MatchesAddress(IPAddress ip)
        {
            return Range.Contains(ip);
        }
    }

    public class DynamicHostRangeFilterRule : HostRangeFilterRule, IDisposable
    {
        private FilterRuleNeeds _needs;

        public required NetworkHostRange Range
        {
            get; init
            {
                (field = value).AddDynamicFilterRule(this);

                if (field is NetworkServiceHostRange range)
                {
                    _needs = new(range.Rules);
                }
            }
        }

        public override bool Matches(IFilterContext context)
        {
            if (base.Matches(context))
            {
                if (Range is NetworkServiceHostRange range)
                {
                    return context is IIPServiceFilterContext && MatchesRules(range.Rules, context, _needs);
                }

                return true;
            }

            return false;
        }

        public override bool MatchesAddress(IPAddress ip)
        {
            return Range.Contains(ip);
        }

        void IDisposable.Dispose()
        {
            Range.RemoveDynamicFilterRule(this);
        }
    }
}
