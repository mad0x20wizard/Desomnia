using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;


namespace MadWizard.Desomnia.Session
{
    public class SessionWatch : ResourceMonitor<SessionProcessWatch>
    {
        [EventContext]
        public required ISession Session { get; init; }

        public required Func<SessionProcessWatchInfo, SessionProcessWatch> CreateProcessWatch { private get; init; }

        public TimeSpan? MaxIdleTime { get; private set; }

        private ClockOptions Clock { get; set; }

        public event EventInvocation? Login;
        public event EventInvocation? RemoteLogin;
        public event EventInvocation? ConsoleLogin;
        public event EventInvocation? RemoteConnect;
        public event EventInvocation? ConsoleConnect;
        public event EventInvocation? Disconnect;
        public event EventInvocation? Unlock;
        public event EventInvocation? Lock;
        public event EventInvocation? Logout;

        public SessionWatch(ISession session)
        {
            session.Connected += Session_Connected;
            session.Disconnected += Session_Disconnected;
            session.Unlocked += Session_Unlocked;
            session.Locked += Session_Locked;
        }

        private void Session_Connected(object? sender, EventArgs e)
        {
            if (Session.IsConsoleConnected)
                TriggerEvent(nameof(ConsoleConnect));
            else if (Session.IsRemoteConnected)
                TriggerEvent(nameof(RemoteConnect));
        }

        private void Session_Disconnected(object? sender, EventArgs e) => TriggerEvent(nameof(Disconnect));
        private void Session_Unlocked(object? sender, EventArgs e) => TriggerEvent(nameof(Unlock));
        private void Session_Locked(object? sender, EventArgs e) => TriggerEvent(nameof(Lock));

        internal void ApplyConfiguration(SessionMonitorConfig config, SessionWatchDescriptor desc)
        {
            if (MaxIdleTime == null || MaxIdleTime.Value < desc.MaxIdleTime)
                MaxIdleTime = desc.MaxIdleTime;

            Clock += desc.MakeClockOptions(config);

            AddEventAction(nameof(Idle), desc.OnIdle);
            AddEventAction(nameof(Login), desc.OnLogin);
            AddEventAction(nameof(RemoteLogin), desc.OnRemoteLogin);
            AddEventAction(nameof(ConsoleLogin), desc.OnConsoleLogin);
            AddEventAction(nameof(RemoteConnect), desc.OnRemoteConnect);
            AddEventAction(nameof(ConsoleConnect), desc.OnConsoleConnect);
            AddEventAction(nameof(Disconnect), desc.OnDisconnect);
            AddEventAction(nameof(Unlock), desc.OnUnlock);
            AddEventAction(nameof(Lock), desc.OnLock);
            AddEventAction(nameof(Logout), desc.OnLogout);

            foreach (var info in desc.Process)
            {
                this.StartTracking(CreateProcessWatch(info));
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var token = new SessionUsage(Session);

            foreach (var processToken in base.InspectResource(interval))
                token.Tokens.Add(processToken);

            if (HadUsageSince(interval) || token.Tokens.Count > 0)
                yield return token;
        }

        private bool HadUsageSince(TimeSpan interval)
        {
            if (Session.IsRemoteConnected && !Clock.Remote)
            {
                return true;
            }
            else if ((Clock.Disconnected || Session.IsConnected) && Session.IdleTime is TimeSpan time)
            {
                if (time < (MaxIdleTime ?? interval))
                {
                    return true;
                }
            }

            return false;
        }

        [ActionHandler("lock")]
        internal void HandleActionLock() => Session.Lock();
        [ActionHandler("logout")]
        internal void HandleActionLogout() => Session.Logoff();
        [ActionHandler("disconnect")]
        internal void HandleActionDisconnect() => Session.Disconnect();

        internal void TriggerLogon()
        {
            TriggerEvent(nameof(Login));

            if (Session.IsRemoteConnected)
            {
                TriggerEvent(nameof(RemoteLogin));
            }
            else if (Session.IsConsoleConnected)
            {
                TriggerEvent(nameof(ConsoleLogin));
            }
        }

        internal void TriggerLogout()
        {
            TriggerEvent(nameof(Logout)); 
        }

        public override void Dispose()
        {
            Session.Locked -= Session_Connected;
            Session.Unlocked -= Session_Disconnected;
            Session.Disconnected -= Session_Disconnected;
            Session.Connected -= Session_Connected;

            base.Dispose();
        }
    }
}
