using MadWizard.Desomnia.Process;
using MadWizard.Desomnia.Process.Manager;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session
{
    public class SessionProcessGroup : ProcessGroup
    {
        [EventContext]
        public ISession Session { get; private init; }

        public SessionProcessGroup(SessionWatch watch, SessionProcessGroupInfo info) : base(info)
        {
            Session = watch.Session;

            // TODO support SessionIdle Events?

            //watch.Idle += (sender, args) => TriggerAction(info.OnSessionIdle);
            //watch.Busy += (sender, args) => ResetActionTimer(info.OnSessionIdle);
        }

        protected override IEnumerable<IProcess> EnumerateProcesses() => Session.Processes;

        protected override ProcessUsage CreateUsageToken(double usage)
        {
            var token = base.CreateUsageToken(usage);

            token.UserName = Session.UserName;

            return token;
        }
    }
}
