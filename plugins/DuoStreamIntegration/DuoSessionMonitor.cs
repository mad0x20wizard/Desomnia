using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Controller;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoSessionMonitor(DuoService service) : ResourceMonitor<DuoInstance>
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5); // how long we wait to startup the context
        private static readonly TimeSpan ActionTimeout  = TimeSpan.FromSeconds(30); // how long we wait for start/stop actions
        private static readonly TimeSpan RetryDelay     = TimeSpan.FromSeconds(10); // delay between StartWatching attempts

        public required ILogger<DuoSessionMonitor> Logger { get; set; }

        public required Func<DuoSettings, Owned<DuoServiceContext>> CreateContext { private get; init; }

        public DuoServiceContext? Context => _ownedContext?.Value;

        private Owned<DuoServiceContext>? _ownedContext;

        readonly AsyncLock _lock = new();

        CancellationTokenSource? _startup;

        bool _disposed;

        internal void Startup()
        {
            service.StatusChanged += Service_StatusChanged;

            if (service.ObservedStatus is ServiceControllerStatus status)
            {
                Service_StatusChanged(service, new(status)); // run initial status logic
            }
            else
            {
                Logger.LogInformation($"Waiting for service to start...");
            }
        }

        private void Service_StatusChanged(object? sender, ServiceStatusChangedEventArgs args)
        {
            try
            {
                using (_lock.Lock()) if (!_disposed)
                {
                    _startup?.Cancel();
                    _startup?.Dispose();
                    _startup = null;

                    switch (args.Status)
                    {
                        case ServiceControllerStatus.Running when service.PID is uint pid:
                            Logger.LogInformation("Service is running at: '{path}' ({version}) -> PID {pid}",
                                service.ExecutablePath, service.Version, pid);

                            if (service.Settings is DuoSettings settings && settings.Instances.Length > 0)
                            {
                                _startup = new();

                                StartWatchingWithRetry(service.Settings, _startup.Token);
                            }

                            break;

                        case ServiceControllerStatus.Stopped when _ownedContext is not null:
                            Logger.LogInformation($"Service has stopped. Monitoring will be suspended.");
                            StopWatching(acquireLock: false);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not handle service status change to: {Status}", args.Status);
            }
        }

        #region Watching start/stop
        private async void StartWatchingWithRetry(DuoSettings settings, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await StartWatching(settings);

                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not start watching Duo service.");

                    StopWatching();

                    try
                    {
                        await Task.Delay(RetryDelay, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task StartWatching(DuoSettings settings)
        {
            using (await _lock.LockAsync()) if (!_disposed)
            {
                _ownedContext?.Dispose(); // make sure to dispose any leftovers

                var context = (_ownedContext = CreateContext(settings)).Value;

                await context.InitializeAsync(StartupTimeout);

                foreach (var instance in context.Instances)
                {
                    StartTracking(instance);
                }

                context.StartWatching();
            }
        }

        private void StopWatching(bool acquireLock = true)
        {
            using (acquireLock ? _lock.Lock() : null)
            {
                if (_ownedContext?.Value is DuoServiceContext context)
                {
                    context.StopWatching();

                    foreach (var instance in context.Instances)
                    {
                        StopTracking(instance);
                    }
                }

                _ownedContext?.Dispose();
                _ownedContext = null;
            }
        }
        #endregion

        #region Instance Action Handlers
        [ActionHandler("start")]
        internal async Task HandleActionStart(DuoInstance instance)
        {
            if (_ownedContext?.Value is DuoServiceContext ctx)
            {
                await ctx.Start(instance, ActionTimeout);
            }
        }

        [ActionHandler("stop", Detached = true)]
        internal async Task HandleActionStop(DuoInstance instance)
        {
            if (_ownedContext?.Value is DuoServiceContext ctx)
            {
                await ctx.Stop(instance, ActionTimeout);
            }
        }
        #endregion

        public override void Dispose()
        {
            _disposed = true;

            _startup?.Cancel();
            _startup = null;

            service.StatusChanged -= Service_StatusChanged;

            StopWatching();

            base.Dispose();
        }
    }
}
