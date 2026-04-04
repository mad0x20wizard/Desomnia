using MadWizard.Desomnia.Pipe.Messages;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Service.Bridge
{
    public static class BridgedTerminalServicesExt
    {
        public static void SendMessage(this ISession session, UserMessage message)
        {
            (((Session)session).Minion)?.Send(message);
        }

        internal static bool CanControlSession(this ISession session, ISession anotherSession)
        {
            if (session == anotherSession && ((Session)session).SessionControl.IsMatchingSelf)
                return true;

            return ((Session)session).SessionControl.Match(session.UserName);
        }

        internal static bool CanControlPower(this ISession session)
        {
            return ((Session)session).PowerControl;
        }
    }
}
