namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class IPFilterRule<T> : FilterRule<T, HostFilterRule> where T : IIPFilterContext
    {

    }
}
