using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Controller;
using Microsoft.Extensions.Logging;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoSessionMonitor(DuoService service) : ResourceMonitor<DuoInstance>
    {
        private static readonly TimeSpan QueryTimeout   = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan UpdateTimeout  = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan RetryDelay     = TimeSpan.FromSeconds(10);

        public required ILogger<DuoSessionMonitor> Logger { get; set; }

        public required Func<DuoSettings, Owned<DuoServiceContext>> CreateContext { private get; init; }

        private Owned<DuoServiceContext>? _context;

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
                lock (this) if (!_disposed)
                {
                    _startup?.Cancel();
                    _startup = null;

                    switch (args.Status)
                    {
                        case ServiceControllerStatus.Running when service.PID is uint pid:
                            Logger.LogInformation("Service is running at: '{path}' ({version}) -> PID {pid}",
                                service.ExecutablePath, service.Version, pid);

                            _startup = new();

                            StartWatchingWithRetry(service.Settings, _startup.Token);

                            break;

                        case ServiceControllerStatus.Stopped when _context is not null:
                            Logger.LogInformation($"Service has stopped. Monitoring will be suspended.");
                            StopWatching();
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
                    StartWatching(settings);

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

        private void StartWatching(DuoSettings settings)
        {
            lock (this) if (!_disposed)
            {
                _context?.Dispose(); // make sure to dispose any leftovers

                _context = CreateContext(settings);
                _context.Value.StartWatching(QueryTimeout);

                foreach (var instance in _context.Value.Instances)
                {
                    StartTracking(instance);
                }
            }
        }

        private void StopWatching()
        {
            lock (this)
            {
                if (_context is not null)
                {
                    foreach (var instance in _context.Value.Instances)
                    {
                        StopTracking(instance);
                    }
                }

                _context?.Dispose();
                _context = null;
            }
        }
        #endregion

        #region Instance Action Handlers
        [ActionHandler("start")]
        internal async Task HandleActionStart(DuoInstance instance)
        {
            if (_context?.Value is DuoServiceContext ctx)
            {
                await ctx.Start(instance, UpdateTimeout);
            }
        }

        [ActionHandler("stop")]
        internal async Task HandleActionStop(DuoInstance instance)
        {
            if (_context?.Value is DuoServiceContext ctx)
            {
                await ctx.Stop(instance, UpdateTimeout);
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
