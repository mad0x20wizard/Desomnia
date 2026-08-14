using Autofac;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using MadWizard.Desomnia.Session.Middleware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MadWizard.Desomnia.Session
{
    public class SessionMonitor(SessionMonitorConfig config, ISessionManager manager) : ResourceMonitor<SessionWatch>, IHostedService
    {
        public required ILogger<SessionMonitor> Logger { get; set; }

        public required ILifetimeScope Scope { private get; init; }

        readonly ConcurrentDictionary<ISession, ILifetimeScope> _sessionScopes = [];

        volatile bool _stopping;

        #region SessionManager events
        private void SessionManager_UserLogin(object? sender, ISession session)
        {
            TrackSession(session, true);
        }
        private void SessionManager_UserLogout(object? sender, ISession session)
        {
            UnTrackSession(session, true);
        }
        #endregion

        async Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            GetEvent(nameof(Idle)).AddAction(config.OnIdle);
            GetEvent(nameof(Demand)).AddAction(config.OnDemand);

            foreach (ISession session in manager)
                TrackSession(session);

            manager.UserLogon += SessionManager_UserLogin;
            manager.UserLogoff += SessionManager_UserLogout;

            Logger.LogDebug("Startup complete");
        }

        #region Session tracking
        private void TrackSession(ISession session, bool logon = false)
        {
            var scope = Scope.BeginLifetimeScope("Session", builder =>
            {
                builder.RegisterType<SessionWatch>()
                    .ConfigurePipeline(p => p.Use(new SessionWatchConfigurator(config)))
                    .AsSelf().SingleInstance();

                builder.RegisterType<AnySessionProcessWatch>().AsSelf();
                builder.RegisterType<SessionProcessWatch>().AsSelf();

                builder.RegisterInstance(session)
                    .As<IProcessManager>()
                    .As<ISession>();
            });

            // publish first — a concurrent logoff must find the key, otherwise its
            // TriggerLogout/StopTracking are lost and the watch lingers as a zombie
            _sessionScopes[session] = scope;

            try
            {
                if (scope.Resolve<SessionWatch>() is SessionWatch watch)
                {
                    if (this.StartTracking(watch) && logon)
                    {
                        watch.TriggerLogon();
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is ObjectDisposedException)
                    // the session logged off while still being tracked — the concurrent
                    // UnTrackSession won the race and disposed the scope under us
                    Logger.LogDebug("Session vanished while being tracked: {Session}", session);
                else
                    // e.g. Win32Exception when WTS already dropped the session, or a
                    // DependencyResolutionException out of the configurator middleware —
                    // neither may escape into the async-void session-change handler
                    Logger.LogError(ex, "Could not track session: {Session}", session);

                UnTrackSession(session); // roll back whatever half-state remains

                return;
            }

            if (_stopping)
            {
                UnTrackSession(session); // late add during shutdown — drain it ourselves
            }
        }

        private void UnTrackSession(ISession session, bool logoff = false)
        {
            if (_sessionScopes.Remove(session, out var scope))
            {
                try
                {
                    if (scope.Resolve<SessionWatch>() is SessionWatch watch)
                    {
                        if (logoff)
                        {
                            watch.TriggerLogout();
                        }

                        this.StopTracking(watch);
                    }
                }
                catch (Exception ex)
                {
                    // a poisoned scope (failed resolve) or a WTS-vanished session must not
                    // block the disposal below, nor escape into async-void event handlers
                    Logger.LogError(ex, "Could not untrack session watch: {Session}", session);
                }

                scope.Dispose();
            }
        }
        #endregion

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            // check if the watches are still backed by the manager (fix for Windows bug)
            foreach (var missing in _sessionScopes.Keys.Except(manager).ToArray())
            {
                UnTrackSession(missing);
            }

            return base.InspectResource(interval);
        }

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;

            manager.UserLogon -= SessionManager_UserLogin;
            manager.UserLogoff -= SessionManager_UserLogout;

            foreach (var session in _sessionScopes.Keys.ToArray())
                UnTrackSession(session);
        }
    }
}
