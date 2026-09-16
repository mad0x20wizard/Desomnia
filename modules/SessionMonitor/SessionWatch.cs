using MadWizard.Desomnia.Configuration;
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
        // The one process-metric collector for this session. It is deliberately not attached as a
        // child resource: its values belong to the session's own expression and Usage token.
        private ProcessWatch? _processWatch;

        [EventContext]
        public required ISession Session { get; init; }

        public required Func<ProcessWatchMetrics, AnySessionProcessWatch>  CreateAnyProcessWatch { private get; init; }
        public required Func<SessionProcessWatchInfo, SessionProcessWatch> CreateProcessWatch    { private get; init; }

        public WatchExpression Watch { get; private set; } = WatchExpression.DefaultOR;

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

        #region Configuration
        internal void ApplyConfiguration(SessionMonitorConfig config, SessionWatchDescriptor desc) =>
            ApplyConfiguration(config, (SessionWatchInfo)desc);

        public void ApplyConfiguration(SessionMonitorConfig config, SessionWatchInfo info)
        {
            Watch <<= info.Watch;

            WatchInput &= info.MakeWatchInputOptions(config);

            Event(nameof(Idle)).AddAction(info.OnIdle);

            Login.AddAction(info.OnLogin);
            RemoteLogin.AddAction(info.OnRemoteLogin);
            ConsoleLogin.AddAction(info.OnConsoleLogin);
            RemoteConnect.AddAction(info.OnRemoteConnect);
            ConsoleConnect.AddAction(info.OnConsoleConnect);
            Disconnect.AddAction(info.OnDisconnect);
            Unlock.AddAction(info.OnUnlock);
            Lock.AddAction(info.OnLock);
            Logout.AddAction(info.OnLogout);

            MergeProcessMetrics(info);

            foreach (var process in info.Process)
            {
                this.StartTracking(CreateProcessWatch(process));
            }
        }

        private void MergeProcessMetrics(ProcessWatchMetrics metrics)
        {
            metrics = new ProcessWatchMetrics(WatchExpression.Yield)
            {
                MinCPU      = metrics.MinCPU       ?? _processWatch?.Metrics?.MinCPU,
                MinGPU      = metrics.MinGPU       ?? _processWatch?.Metrics?.MinGPU,
                MinIO       = metrics.MinIO        ?? _processWatch?.Metrics?.MinIO,
                MinTraffic  = metrics.MinTraffic   ?? _processWatch?.Metrics?.MinTraffic,
            };

            if (metrics != _processWatch?.Metrics)
            {
                _processWatch?.Dispose();
                _processWatch = null;

                if (metrics.HasThresholds)
                {
                    _processWatch = CreateAnyProcessWatch(metrics);
                }
            }
        }
        #endregion

        #region Inspection
        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var usage = new SessionUsage(Session) { Metrics = CollectMetrics(interval) };

            foreach (var token in base.InspectResource(interval))
            {
                usage.Tokens.Add(token);
            }

            if (Watch.IsYield || Watch.Evaluate(usage.Metrics) || usage.Tokens.Count > 0)
            {
                yield return usage;
            }
        }

        private SessionMetricsUsage CollectMetrics(TimeSpan interval)
        {
            var metrics = new SessionMetricsUsage(CollectProcessMetrics(interval));

            if (WatchInput is WatchInputOptions watch)
            {
                var matches = false;

                if (Session.IsRemoteConnected && !watch.Remote)
                {
                    matches = true;
                }
                else if (watch.Disconnected || Session.IsConnected)
                {
                    if (Session.IdleTime is not TimeSpan time)
                        throw new InvalidOperationException("Configured session metric 'Input' cannot be read.");

                    if (matches = time < (watch.MaxLastInputTime ?? interval))
                    {
                        metrics.LastInputTime = time;
                    }
                }

                metrics.Add("Input", matches);
            }

            return metrics;
        }

        private ProcessMetricsUsage CollectProcessMetrics(TimeSpan interval)
        {
            if (_processWatch?.Inspect(interval).SingleOrDefault() is ProcessUsage process)
            {
                if (process.Metrics is ProcessMetricsUsage metrics)
                {
                    return metrics;
                }
            }

            return new ProcessMetricsUsage(interval);
        }
        #endregion

        #region Inspection Synchronization
        protected override void HandleInspectionResult(TimeSpan duration, IEnumerable<UsageToken> tokens)
        {
            if (!Watch.IsYield)
            {
                base.HandleInspectionResult(duration, tokens);
            }
        }

        public void InjectInspectionResult(TimeSpan duration, IEnumerable<UsageToken> tokens)
        {
            if (!Watch.IsYield)
                throw new InvalidOperationException("Only a yielding SessionWatch can accept an external inspection result.");

            base.HandleInspectionResult(duration, tokens);
        }
        #endregion

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

            _processWatch?.Dispose();

            base.Dispose();
        }
    }
}
