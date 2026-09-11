using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Controller;
using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoSessionMonitor(DuoService service) : ResourceMonitor<DuoInstance>, IStartable
    {
        private static readonly TimeSpan QueryTimeout   = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan UpdateTimeout  = TimeSpan.FromSeconds(20);

        public required ILogger<DuoSessionMonitor> Logger { get; set; }

        public required Func<DuoSettings, Owned<DuoServiceContext>> CreateContext { private get; init; }

        private Owned<DuoServiceContext>? _context;

        private Lock _lock = new();
        private bool _disposed;

        void IStartable.Start()
        {
            Logger.LogInformation($"Monitor is enabled. Waiting for service to start...");

            service.StatusChanged += Service_StatusChanged;

            if (service.ObservedStatus is ServiceControllerStatus status)
            {
                Service_StatusChanged(service, new(status)); // aus IStartable entfernen?
            }
        }

        private void Service_StatusChanged(object? sender, ServiceStatusChangedEventArgs args)
        {
            try
            {
                lock (_lock) if (!_disposed) switch (args.Status)
                {
                    case ServiceControllerStatus.Running when service.PID is uint pid:
                        Logger.LogInformation("Service is running at: '{path}' ({version}) -> PID {pid}", 
                            service.ExecutablePath, service.Version, pid);

                        try
                        {
                            StartWatching(service.Settings);
                        }
                        catch
                        {
                            StopWatching();
                            throw;
                        }

                        break;

                    case ServiceControllerStatus.Stopped when _context is not null:
                        Logger.LogInformation($"Service has stopped. Monitoring will be suspended.");
                        StopWatching();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not handle service status change to: {Status}", args.Status);
            }
        }

        private void StartWatching(DuoSettings settings)
        {
            lock (_lock)
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
            lock (_lock)
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

            service.StatusChanged -= Service_StatusChanged;

            StopWatching();

            base.Dispose();
        }
    }
}
