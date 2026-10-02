using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Session.Strategy
{
    internal class HeadlessStrategy(IEnumerable<DuoInstance> instances) : IInstanceSessionStrategy
    {
        public bool this[DuoInstance instance, ISession session]
        {
            get
            {
                if (session.IsHeadless)
                {
                    if (instance.Settings.UserName == session.UserName)
                    {
                        if (instances.Count(i => i.Settings.UserName == instance.Settings.UserName) == 1)
                        {
                            return true; // only if the match is unambiguous
                        }
                    }
                }

                return false;
            }
        }
    }
}
