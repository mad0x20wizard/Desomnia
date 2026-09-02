using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;


namespace MadWizard.Desomnia.Session
{
    public class SessionWatch : ResourceMonitor<ProcessWatch>
    {
        /**
         * The session-wide metric watches – one per descriptor that configured a threshold.
         *
         * A list rather than a single watch because a session collects its configuration from every
         * selector that matched it (Everyone, its user, Administrator – and the Duo plugin, which
         * applies an instance's info to an already-configured watch). Keeping one would mean the
         * last of them silently replacing the thresholds of the others, and leaving the replaced
         * watch subscribed to the manager's process events for the life of the session.
         *
         * They are deliberately not tracked as children: a child's token would be listed beside the
         * declared process groups, where this is the session's own measurement and belongs on the
         * session's token. So they are inspected by hand below – but not disposed, because they are
         * resolved from the session's own lifetime scope and the container disposes them with it.
         */
        private readonly List<AnySessionProcessWatch> _aggregates = [];

        [EventContext]
        public required ISession Session { get; init; }

        public required Func<ProcessWatchMetrics, AnySessionProcessWatch>   CreateAnyProcessWatch   { private get; init; }
        public required Func<SessionProcessWatchInfo, SessionProcessWatch>  CreateProcessWatch      { private get; init; }

        private WatchOperator _watchOperator = WatchOperator.OR;

        private TimeSpan? _maxLastInputTime;
        private TimeSpan? _minLastInputTime;

        private TimeSpan? MaxLastInputTime => _watchOperator == WatchOperator.AND ? _minLastInputTime : _maxLastInputTime;

        private WatchInputOptions? WatchInput { get; set; } = new();

        public event EventInvocation? Login;
        public event EventInvocation? RemoteLogin;
        public event EventInvocation? ConsoleLogin;
        public event EventInvocation? RemoteConnect;
        public event EventInvocation? ConsoleConnect;

        [EventOpposite(nameof(ConsoleConnect), nameof(RemoteConnect))]
        public event EventInvocation? Disconnect;

        [EventOpposite(nameof(Unlock))]
        public event EventInvocation? Lock;
        public event EventInvocation? Unlock;

        [EventOpposite(nameof(Login), nameof(ConsoleLogin), nameof(RemoteLogin))]
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
                ConsoleConnect.TriggerEvent();
            else if (Session.IsRemoteConnected)
                RemoteConnect.TriggerEvent();
        }

        private void Session_Disconnected(object? sender, EventArgs e) => Disconnect.TriggerEvent();
        private void Session_Unlocked(object? sender, EventArgs e) => Unlock.TriggerEvent();
        private void Session_Locked(object? sender, EventArgs e) => Lock.TriggerEvent();

        internal void ApplyConfiguration(SessionMonitorConfig config, SessionWatchDescriptor desc) => 
            ApplyConfiguration(config, (SessionWatchInfo)desc);

        public void ApplyConfiguration(SessionMonitorConfig config, SessionWatchInfo info)
        {
            _watchOperator = info.Watch == WatchOperator.AND ? info.Watch : _watchOperator;

            if (_maxLastInputTime == null || _maxLastInputTime < info.MaxLastInputTime)
                _maxLastInputTime = info.MaxLastInputTime;
            if (_minLastInputTime == null || _minLastInputTime > info.MaxLastInputTime)
                _minLastInputTime = info.MaxLastInputTime;

            WatchInput += info.MakeWatchInputOptions(config);

            GetEvent(nameof(Idle)).AddAction(info.OnIdle);

            Login.AddAction(info.OnLogin);
            RemoteLogin.AddAction(info.OnRemoteLogin);
            ConsoleLogin.AddAction(info.OnConsoleLogin);
            RemoteConnect.AddAction(info.OnRemoteConnect);
            ConsoleConnect.AddAction(info.OnConsoleConnect);
            Disconnect.AddAction(info.OnDisconnect);
            Unlock.AddAction(info.OnUnlock);
            Lock.AddAction(info.OnLock);
            Logout.AddAction(info.OnLogout);

            if (info.HasThresholds)
            {
                _aggregates.Add(CreateAnyProcessWatch(info));
            }

            foreach (var process in info.Process)
            {
                this.StartTracking(CreateProcessWatch(process));
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var usage = new SessionUsage(Session);

            foreach (var token in base.InspectResource(interval))
            {
                usage.Tokens.Add(token);
            }

            if (HadUsageSince(usage, interval) || usage.Tokens.Count > 0)
            {
                yield return usage;
            }
        }

        private bool HadUsageSince(SessionUsage usage, TimeSpan interval)
        {
            bool needsMatch = false;

            int matchesMetrics = 0;
            if (_aggregates.Count > 0) // user specified at least one process metric
            {
                needsMatch = true;

                foreach (var process in _aggregates.Select(a => a.Inspect(interval).FirstOrDefault()).OfType<ProcessUsage>())
                {
                    if (process.Metrics is ProcessUsageMetrics metrics)
                    {
                        usage.Metrics += metrics;

                        matchesMetrics++;
                    }
                }
            }

            bool? matchesInput = null;
            if (WatchInput is WatchInputOptions watch)
            {
                needsMatch = true;

                matchesInput = false;
                if (Session.IsRemoteConnected && !watch.Remote)
                {
                    matchesInput = true;
                }
                else if ((watch.Disconnected || Session.IsConnected) && Session.IdleTime is TimeSpan time)
                {
                    if (time < (MaxLastInputTime ?? interval))
                    {
                        usage.LastInputTime = time;

                        matchesInput = true;
                    }
                }
            }

            if (needsMatch)
            {
                switch (_watchOperator)
                {
                    case WatchOperator.OR:
                        return matchesInput == true || matchesMetrics > 0;

                    case WatchOperator.AND:
                        return matchesInput != false && matchesMetrics == _aggregates.Count;
                }

                throw new InvalidOperationException($"Unknown WatchOperator = {_watchOperator}");
            }

            return true;
        }

        [ActionHandler("lock")]
        internal void HandleActionLock() => Session.Lock();
        [ActionHandler("logout")]
        internal void HandleActionLogout() => Session.Logoff();
        [ActionHandler("disconnect")]
        internal void HandleActionDisconnect() => Session.Disconnect();

        internal void TriggerLogon()
        {
            Login.TriggerEvent();

            if (Session.IsRemoteConnected)
            {
                RemoteLogin.TriggerEvent();
            }
            else if (Session.IsConsoleConnected)
            {
                ConsoleLogin.TriggerEvent();
            }
        }

        internal void TriggerLogout()
        {
            Logout.TriggerEvent(); 
        }

        public override void Dispose()
        {
            Session.Locked -= Session_Locked;
            Session.Unlocked -= Session_Unlocked;
            Session.Disconnected -= Session_Disconnected;
            Session.Connected -= Session_Connected;

            base.Dispose();
        }
    }
}
