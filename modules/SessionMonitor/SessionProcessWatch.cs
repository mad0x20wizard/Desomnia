using MadWizard.Desomnia.Process;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public class SessionProcessWatch : ProcessWatch
    {
        [EventContext]
        public required ISession Session { get; init; }

        public SessionProcessWatch(SessionProcessWatchInfo info) : base(info)
        {
            // TODO support SessionIdle Events?

            //watch.Idle += (sender, args) => TriggerAction(info.OnSessionIdle);
            //watch.Busy += (sender, args) => ResetActionTimer(info.OnSessionIdle);
        }
    }
}
