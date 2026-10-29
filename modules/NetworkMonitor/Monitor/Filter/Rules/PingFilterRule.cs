namespace MadWizard.Desomnia.Network.Filter.Rules
{
    internal class PingFilterRule : IPFilterRule<IICMPFilterContext>
    {
        override public bool Matches(IICMPFilterContext context)
        {
            if (context.Type == ICMPType.Echo)
            {
                return base.Matches(context);
            }

            return false;
        }
    }
}
