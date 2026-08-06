using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Processes.Configuration;

namespace MadWizard.Desomnia.Session.Configuration
{
    public class SessionWatchInfo : ProcessWatchMetrics
    {
        public TimeSpan? MaxIdleTime { get; set; }

        #region Session :: ClockOptions
        protected bool? ClockTime { get; set; }
        protected bool? ClockRemote { get; set; }
        protected bool? ClockDisconnected { get; set; }

        public virtual ClockOptions MakeClockOptions(SessionMonitorConfig config) => new()
        {
            Time = this.ClockTime ?? config.ClockTime,
            Remote = this.ClockRemote ?? config.ClockRemote,
            Disconnected = this.ClockDisconnected ?? config.ClockDisconnected,
        };
        #endregion

        public DelayedActionInfo? OnIdle { get; set; }

        public ScheduledActionInfo? OnLogin { get; set; }
        public ScheduledActionInfo? OnRemoteLogin { get; set; }
        public ScheduledActionInfo? OnConsoleLogin { get; set; }
        public ScheduledActionInfo? OnRemoteConnect { get; set; }
        public ScheduledActionInfo? OnConsoleConnect { get; set; }
        public ScheduledActionInfo? OnDisconnect { get; set; }
        public ScheduledActionInfo? OnUnlock { get; set; }
        public ScheduledActionInfo? OnLock { get; set; }
        public ScheduledActionInfo? OnLogout { get; set; }

        public IList<SessionProcessWatchInfo> Process { get; set; } = [];
    }
}
