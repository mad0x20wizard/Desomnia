using Autofac;
using MadWizard.Desomnia.Process.Manager;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Session
{
    public class SessionMonitor(SessionMonitorConfig config, ISessionManager manager) : ResourceMonitor<SessionWatch>, IStartable
    {
        public required ILogger<SessionMonitor> Logger { get; set; }

        public required ILifetimeScope Scope { private get; init; }

        readonly Dictionary<ISession, ILifetimeScope> _sessionScopes = [];

        public void Start()
        {
            manager.ToString();

            foreach (ISession session in manager)
                TrackSession(session);

            manager.UserLogon += SessionManager_UserLogin;
            manager.UserLogoff += SessionManager_UserLogout;

            Logger.LogDebug("Startup complete");
        }

        #region SessionManager events
        private void SessionManager_UserLogin(object? sender, ISession session)
        {
            TrackSession(session, true);
        }
        private void SessionManager_UserLogout(object? sender, ISession session)
        {
            foreach (var scope in _sessionScopes.Where(w => w.Key == session).Select(w => w.Value))
            {
                if (scope.Resolve<SessionWatch>() is SessionWatch watch)
                {
                    watch.TriggerLogout();

                    this.StopTracking(watch);
                }

                _sessionScopes.Remove(session);

                scope.Dispose();
            }
        }
        #endregion

        private void TrackSession(ISession session, bool logon = false)
        {
            var scope = Scope.BeginLifetimeScope("Session", builder =>
            {
                builder.RegisterType<SessionWatch>().AsSelf().SingleInstance();

                builder.RegisterType<SessionProcessWatch>().AsSelf();

                builder.RegisterInstance(session)
                    .As<IProcessManager>()
                    .As<ISession>();
            });

            if (scope.Resolve<SessionWatch>() is SessionWatch watch)
            {
                config.Configure(session, watch.ApplyConfiguration);

                if (this.StartTracking(watch) && logon)
                {
                    watch.TriggerLogon();
                }
            }

            _sessionScopes[session] = scope;
        }
    }
}
