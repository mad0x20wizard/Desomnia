using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Filter
{
    public interface IFilter
    {
        IEnumerable<FilterRule> Rules => [];

        bool ShouldFilter(IFilterContext context);
    }

    internal class CompositeFilter(IEnumerable<IFilter> filters) : IFilter
    {
        IEnumerable<FilterRule> IFilter.Rules => filters.SelectMany(x => x.Rules);

        bool IFilter.ShouldFilter(IFilterContext context)
        {
            AdditionalDataNeededException? pending = null;

            foreach (IFilter filter in filters)
            {
                try
                {
                    if (filter.ShouldFilter(context))
                        return true;
                }
                catch (AdditionalDataNeededException needed)
                {
                    pending ??= needed;
                }
            }

            return pending is null ? false : throw pending;
        }
    }
}
