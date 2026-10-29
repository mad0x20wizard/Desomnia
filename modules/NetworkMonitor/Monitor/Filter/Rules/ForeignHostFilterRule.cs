using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    internal class ForeignHostFilterRule : EveryHostFilterRule
    {
        public required LocalNetworkRange LocalRange { get; init; }

        override public bool Matches(IFilterContext context)
        {
            if (!LocalRange.Contains(context.RemoteAddress))
            {
                return base.Matches(context);
            }

            return false;
        }
    }
}
