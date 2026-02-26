using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;


namespace MadWizard.Desomnia.Session
{
    public class SessionWatch : ResourceMonitor<SessionProcessGroup>
    {
        [EventContext]
        public ISession Session { get; private init; }

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

        internal bool ShouldBeTracked => true;

        public SessionWatch(ISession session)
        {
            Session = session;

            Session.Connected += Session_Connected;
            Session.Disconnected += Session_Disconnected;
            Session.Unlocked += Session_Unlocked;
            Session.Locked += Session_Locked;
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
                this.StartTracking(new SessionProcessGroup(this, info));
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (Session.IsRemoteConnected && !Clock.Remote)
            {
                yield return new SessionUsageToken(Session.UserName, Session.ClientName);
            }
            else if ((Clock.Disconnected || Session.IsConnected) && Session.IdleTime is TimeSpan time)
            {
                if (time < (MaxIdleTime ?? interval))
                {
                    yield return new SessionUsageToken(Session.UserName, Session.ClientName);
                }
            }

            foreach (var token in base.InspectResource(interval))
            {
                yield return token;
            }
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

        protected override Task TriggerEventAsync(Event @event) // TODO remove
        {
            return base.TriggerEventAsync(@event);
        }
    }
}
