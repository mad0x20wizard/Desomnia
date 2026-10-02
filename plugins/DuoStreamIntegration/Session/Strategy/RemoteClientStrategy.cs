using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Duo.Session.Strategy
{
    internal class RemoteClientStrategy : IInstanceSessionStrategy
    {
        public bool this[DuoInstance instance, ISession session]
        {
            get
            {
                if (session.IsRemoteConnected)
                {
                    if (instance.Settings.UserName == session.UserName)
                    {
                        return instance.Name == session.ClientName;
                    }
                }

                return false;
            }
        }
    }
}