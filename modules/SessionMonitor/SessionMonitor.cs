using Autofac;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Session
{
    public class SessionMonitor(SessionMonitorConfig config, ISessionManager manager) : ResourceMonitor<SessionWatch>, IStartable
    {
        public required ILogger<SessionMonitor> Logger { get; set; }

        public void Start()
        {
            foreach (ISession session in manager)
                MayBeTrackSession(session);

            manager.UserLogon += SessionManager_UserLogin;
            manager.UserLogoff += SessionManager_UserLogout;

            Logger.LogDebug("Startup complete");
        }

        private void SessionManager_UserLogin(object? sender, ISession session)
        {
            MayBeTrackSession(session, true);
        }
        private void SessionManager_UserLogout(object? sender, ISession session)
        {
            foreach (var watch in this)
                if (watch?.Session == session)
                    this.StopTracking(watch);
        }

        private void MayBeTrackSession(ISession session, bool logon = false)
        {
            var watch = new SessionWatch(session);

            config.Configure(session, watch.ApplyConfiguration);

            if (watch.ShouldBeTracked)
            {
                if (this.StartTracking(watch) && logon)
                {
                    watch.TriggerLogon();
                }
            }
        }
    }
}
