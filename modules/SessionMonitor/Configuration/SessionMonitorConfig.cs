using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Session.Manager;

namespace MadWizard.Desomnia.Session.Configuration
{
    public record SessionWatchDescriptor : SessionWatchInfo
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

            // SOMEDAY add group

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
        public DelayedActionInfo? OnUsage { get; set; }

        internal bool RegisterWithSleepProxy { get; set; } = true;

        #region SessionMonitor :: WatchInputOptions
        internal bool WatchInput                { get; set; } = true;
        internal bool WatchInputRemote          { get; set; } = false;
        internal bool WatchInputDisconnected    { get; set; } = false;

        internal TimeSpan? MaxLastInputTime     { get; set; } = null;
        #endregion
    }
}
