using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session.Configuration
{
    public class SessionWatchDescriptor : SessionWatchInfo
    {
        public SessionSelector? Name { get; set; }
    }

    public abstract class SessionMonitorConfig<TConfig, TDesc>
        where TConfig : SessionMonitorConfig<TConfig, TDesc>
        where TDesc : SessionWatchDescriptor
    {
        public delegate void ConfigureWithDescriptor(TConfig config, TDesc desc);

        public TDesc? Everyone { get; set; }
        public TDesc? Administrator { get; set; }

        public IList<TDesc> User { get; set; } = [];

        public void Configure<S>(S session, ConfigureWithDescriptor configure) where S : ISession
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

    public class SessionMonitorConfig : SessionMonitorConfig<SessionMonitorConfig, SessionWatchDescriptor>
    {
        public DelayedActionInfo? OnIdle { get; set; }
        public DelayedActionInfo? OnDemand { get; set; }

        internal bool RegisterWithSleepProxy { get; set; } = true;

        #region SessionMonitor :: ClockOptions
        internal bool ClockTime         { get; set; } = true;
        internal bool ClockRemote       { get; set; } = false;
        internal bool ClockDisconnected { get; set; } = false;
        #endregion
    }
}