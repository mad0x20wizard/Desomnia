using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Sunshine;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoStreamMonitor(DuoStreamMonitorConfig config, DuoManager manager, ISessionManager sessionManager) : ResourceMonitor<DuoInstance>, IStartable
    {
        public required ILogger<DuoStreamMonitor> Logger { get; set; }

        public required IComponentContext Context { get; set; }

        public required Func<ushort, SunshineServiceWatch>          CreateSunshineWatch { private get; init; }
        public required Func<ushort, SunshineServiceWatchFallback>  CreateSunshineWatchFallback { private get; init; }

        private DynamicNetworkObserver? DynamicNetworkMonitor   => Context.ResolveOptional<DynamicNetworkObserver>();
        private SessionMonitor?         SessionMonitor          => Context.ResolveOptional<SessionMonitor>();

        private bool IsFallbackMode => config.UseFallback || (DynamicNetworkMonitor is null);

        void IStartable.Start()
        {
            if (DynamicNetworkMonitor != null)
            {
                DynamicNetworkMonitor.MonitoringStarted += DynamicNetworkMonitor_MonitoringStarted;
            }

            if (SessionMonitor != null)
            {
                SessionMonitor.Filters += SessionMonitor_Filter;
            }

            sessionManager.UserLogon += SessionManager_UserLogon;
            sessionManager.UserLogoff += SessionManager_UserLogoff;

            manager.Started += DuoService_Started;
            manager.Stopped += DuoService_Stopped;

            Logger.LogInformation($"Monitor is enabled. Waiting for service to start...");
        }

        private void DynamicNetworkMonitor_MonitoringStarted(object? sender, NetworkMonitor monitor)
        {
            foreach (var instance in this)
            {
                RegisterInstanceWithNetworkMonitor(instance, monitor);
            }
        }

        private void DuoService_Started(object? sender, EventArgs e)
        {
            Logger.LogInformation($"Service is running:");

            foreach (var instance in manager)
            {
                instance.Started += DuoInstance_Started;
                instance.Stopped += DuoInstance_Stopped;

                SunshineServiceWatch sunshine;

                if (IsFallbackMode)
                {
                    if (!instance.IsSandboxed)
                    {
                        Logger.LogInformation($"Monitoring {instance}:{instance.Port} -> using fallback");

                        Context.Resolve<SunshineServiceWatchFallback>(TypedParameter.From(instance.Port));

                        instance.StartTracking(sunshine = CreateSunshineWatchFallback(instance.Port));
                    }
                    else
                    {
                        Logger.LogWarning($"NOT Monitoring {instance}:{instance.Port} -> fallback is not available for sandboxed instances");

                        continue;
                    }
                }
                else
                {
                    Logger.LogInformation($"Monitoring {instance}:{instance.Port}");

                    instance.StartTracking(sunshine = CreateSunshineWatch(instance.Port));

                    foreach (var monitor in DynamicNetworkMonitor!)
                    {
                        RegisterInstanceWithNetworkMonitor(instance, monitor);
                    }
                }

                // prepare Instance, if Desomnia was started after it
                StopSessionTracking(instance);

                sunshine.Inspect(TimeSpan.Zero); // trigger WaitForClient(), if in Fallback-Mode and Sunshine is not running

                this.StartTracking(instance);
            }
        }

        private async Task DuoInstance_Started(Event eventObj)
        {
            var instance = (DuoInstance)eventObj.Source!;

            foreach (var service in instance)
                if (service is SunshineServiceWatchFallback fallback)
                    fallback.StopWaiting();

            /**
             * For older versions of Duo (< 1.5.2).
             */
            if (instance.SessionID != null)
            {
                StopSessionTracking(instance);
            }
        }

        private async Task DuoInstance_Stopped(Event eventObj)
        {
            var instance = (DuoInstance)eventObj.Source!;

            foreach (var service in instance)
                if (service is SunshineServiceWatchFallback fallback)
                    fallback.WaitForClient();
        }

        private void DuoService_Stopped(object? sender, EventArgs e)
        {
            Logger.LogInformation($"Service has stopped. Monitoring will be suspended.");

            foreach (var instance in this)
            {
                foreach (var service in instance)
                {
                    foreach (var monitor in DynamicNetworkMonitor?.ToArray() ?? [])
                    {
                        monitor.OfType<LocalHostWatch>().FirstOrDefault()?.StopTracking(service);
                    }

                    instance.StopTracking(service);

                    service.Dispose();
                }

                this.StopTracking(instance);
            }
        }

        private static void RegisterInstanceWithNetworkMonitor(DuoInstance instance, NetworkMonitor monitor)
        {
            if (monitor.OfType<LocalHostWatch>().FirstOrDefault() is LocalHostWatch watch)
            {
                foreach (var service in instance)
                {
                    watch.StartTracking(service, false);
                }
            }
        }

        #region Session Monitoring
        /**
         * Prevent monitoring of sessions that are already being monitored by DuoStreamMonitor.
         */
        private bool SessionMonitor_Filter(SessionWatch watch)
        {
            if (this.Where(instance => instance.HasInitiated(watch.Session)).Any())
                return false;

            return true;
        }

        private void SessionManager_UserLogon(object? sender, ISession session)
        {
            if (this.Where(instance => instance.HasInitiated(session)).FirstOrDefault() is DuoInstance instance)
                instance.Session = session;
        }
        private void SessionManager_UserLogoff(object? sender, ISession session)
        {
            if (this.Where(instance => instance.Session == session).FirstOrDefault() is DuoInstance instance)
                instance.Session = null;
        }

        private void StopSessionTracking(DuoInstance instance)
        {
            if ((instance.Session = sessionManager.Where(instance.HasInitiated).FirstOrDefault()) != null)
            {
                if (SessionMonitor?.Where(s => s.Session.Id == instance.Session.Id).FirstOrDefault() is SessionWatch watch)
                {
                    SessionMonitor.StopTracking(watch);
                }
            }
        }
        #endregion

        #region Instance Action Handlers
        [ActionHandler("start")]
        internal async Task HandleActionStart(DuoInstance instance)
        {
            if (await instance.Semaphore.WaitAsync(0))
            {
                try
                {
                    if (instance.IsRunning == false)
                        await manager.Start(instance);
                }
                finally
                {
                    instance.Semaphore.Release();
                }
            }
        }

        [ActionHandler("stop")]
        internal async Task HandleActionStop(DuoInstance instance)
        {
            if (await instance.Semaphore.WaitAsync(0))
            {
                try
                {
                    if (instance.IsRunning == true)
                        await manager.Stop(instance);
                }
                finally
                {
                    instance.Semaphore.Release();
                }
            }
        }
        #endregion
    }
}
