using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;


namespace MadWizard.Desomnia.Session
{
    public class SessionWatch(ISession session) : ResourceMonitor<SessionProcessGroup>
    {
        [EventContext]
        public ISession Session => session;

        internal bool ShouldBeTracked => true;

        public event EventInvocation? Login;
        public event EventInvocation? RemoteLogin;
        public event EventInvocation? ConsoleLogin;

        internal void ApplyConfiguration(SessionWatchDescriptor desc)
        {
            AddEventAction(nameof(Idle), desc.OnIdle);
            AddEventAction(nameof(Login), desc.OnLogin);
            AddEventAction(nameof(RemoteLogin), desc.OnRemoteLogin);
            AddEventAction(nameof(ConsoleLogin), desc.OnConsolesLogin);

            foreach (var info in desc.Process)
            {
                this.StartTracking(new SessionProcessGroup(this, info));
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (session.IsRemoteConnected)
            {
                yield return new SessionUsageToken(session.UserName, session.ClientName);
            }
            else if (session.IsConnected && session.IdleTime is TimeSpan time)
            {
                if (time < interval)
                {
                    yield return new SessionUsageToken(session.UserName);
                }
            }

            foreach (var token in base.InspectResource(interval))
                yield return token;
        }

        [ActionHandler("lock")]
        internal void HandleActionLock() => session.Lock();
        [ActionHandler("logout")]
        internal void HandleActionLogout() => session.Logoff();
        [ActionHandler("disconnect")]
        internal void HandleActionDisconnect() => session.Disconnect();

        public void TriggerLogon()
        {
            TriggerEvent(nameof(Login));

            if (session.IsRemoteConnected)
            {
                TriggerEvent(nameof(RemoteLogin));
            }
            else if (session.IsConsoleConnected)
            {
                TriggerEvent(nameof(ConsoleLogin));
            }
        }
    }
}
