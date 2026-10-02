using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Session.Strategy
{
    internal class CompositeStrategy(IEnumerable<IInstanceSessionStrategy> strategies) : IInstanceSessionStrategy
    {
        public bool this[DuoInstance instance, ISession session]
        {
            get
            {
                foreach (var strategy in strategies)
                    if (strategy[instance, session])
                        return true;

                return false;
            }
        }
    }
}
