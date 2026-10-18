namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class FilterRule
    {
        public required FilterRuleType Type { get; init; }
    }

    public abstract class FilterRule<T> : FilterRule where T : IFilterContext
    {
        public abstract bool Matches(T context);
    }
}
