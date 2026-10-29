using MadWizard.Desomnia.Network.Neighborhood;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class HostFilterRule : FilterRule<IFilterContext>
    {
        public abstract bool MatchesAddress(IPAddress ip);

        override public bool Matches(IFilterContext context)
        {
            return MatchesAddress(context.RemoteAddress);
        }
    }

    public class StaticHostFilterRule : HostFilterRule
    {
        public required IEnumerable<IPAddress> Addresses { get; init; }

        public override bool MatchesAddress(IPAddress ip)
        {
            return Addresses.Contains(ip);
        }
    }

    public class DynamicHostFilterRule : HostFilterRule, IDisposable
    {
        public required NetworkHost Host
        {
            get; init
            {
                (field = value).AddDynamicFilterRule(this);
            }
        }

        public override bool MatchesAddress(IPAddress ip)
        {
            return Host.HasAddress(ip);
        }

        void IDisposable.Dispose()
        {
            Host.RemoveDynamicFilterRule(this);
        }
    }
}
