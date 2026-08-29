using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Processes.Configuration;

namespace MadWizard.Desomnia.Session.Configuration
{
    public class SessionWatchInfo : ProcessWatchMetrics
    {
        public TimeSpan? MaxLastInputTime { get; set; }

        #region Session :: WatchInputOptions
        protected bool? WatchInput { get; set; }
        protected bool? WatchInputRemote { get; set; }
        protected bool? WatchInputDisconnected { get; set; }

        public virtual WatchInputOptions? MakeWatchInputOptions(SessionMonitorConfig config) =>
            this.WatchInput ?? config.WatchInput ? new()
            {
                Remote = this.WatchInputRemote ?? config.WatchInputRemote,
                Disconnected = this.WatchInputDisconnected ?? config.WatchInputDisconnected,
            } : null;
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
