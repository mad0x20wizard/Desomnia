using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session.Configuration
{
    public abstract class SessionMonitorConfig<TConfig, TDesc>
        where TConfig : SessionMonitorConfig<TConfig, TDesc>
        where TDesc : SessionDescriptor
    {
        public DelayedAction? OnIdle { get; set; }
        public DelayedAction? OnDemand { get; set; }

        public delegate void ConfigureWithDescriptior(TConfig config, TDesc desc);

        public TDesc? Everyone { get; set; }
        public TDesc? Administrator { get; set; }

        public IList<TDesc> User { get; set; } = [];

        public void Configure<S>(S session, ConfigureWithDescriptior configure) where S : ISession
        {
            var self = (TConfig)this; // safe if inheritance is correct

            if (this.Everyone is TDesc desc)
                configure(self, desc);

            if (session.IsUser)
                foreach (var userDesc in this.User)
                    if (userDesc.Name?.Match(session.UserName) ?? true)
                        configure(self, userDesc);

            if (session.IsAdministrator)
                if (this.Administrator is TDesc adminDesc)
                    configure(self, adminDesc);
        }
    }

    public abstract class SessionDescriptor
    {
        public SessionMatcher? Name { get; set; }
    }

    public class SessionWatchDescriptor : SessionDescriptor
    {
        public TimeSpan? MaxIdleTime { get; set; }

        #region Session :: ClockOptions
        private bool? ClockRemote { get; set; }
        private bool? ClockDisconnected { get; set; }

        public virtual ClockOptions MakeClockOptions(SessionMonitorConfig monitor) => new()
        {
            Remote = this.ClockRemote ?? monitor.ClockRemote,
            Disconnected = this.ClockDisconnected ?? monitor.ClockDisconnected,
        };
        #endregion

        public ScheduledAction? OnIdle { get; set; }
        public ScheduledAction? OnLogin { get; set; }
        public ScheduledAction? OnRemoteLogin { get; set; }
        public ScheduledAction? OnConsoleLogin { get; set; }
        public ScheduledAction? OnRemoteConnect { get; set; }
        public ScheduledAction? OnConsoleConnect { get; set; }
        public ScheduledAction? OnDisconnect { get; set; }
        public ScheduledAction? OnUnlock { get; set; }
        public ScheduledAction? OnLock { get; set; }
        public ScheduledAction? OnLogout { get; set; }

        public IList<SessionProcessWatchInfo> Process { get; set; } = [];
    }

    public class SessionMonitorConfig : SessionMonitorConfig<SessionMonitorConfig, SessionWatchDescriptor>
    {
        #region SessionMonitor :: ClockOptions
        internal bool ClockRemote { get; set; } = false;
        internal bool ClockDisconnected { get; set; } = false;
        #endregion
    }
}