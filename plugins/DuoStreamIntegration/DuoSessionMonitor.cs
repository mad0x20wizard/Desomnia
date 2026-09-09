using Autofac;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoSessionMonitor(DuoManager manager) : ResourceMonitor<DuoInstance>, IStartable
    {
        public required ILogger<DuoSessionMonitor> Logger { get; set; }

        void IStartable.Start()
        {
            Logger.LogInformation($"Monitor is enabled. Waiting for service to start...");

            manager.Started += DuoService_Started;
            manager.Stopped += DuoService_Stopped;
        }

        private void DuoService_Started(object? sender, DuoLifecycleEventArgs args)
        {
            foreach (var instance in args.Instances)
            {
                this.StartTracking(instance);
            }
        }

        private void DuoService_Stopped(object? sender, DuoLifecycleEventArgs args)
        {
            Logger.LogInformation($"Service has stopped. Monitoring will be suspended.");

            foreach (var instance in args.Instances)
            {
                this.StopTracking(instance);
            }
        }

        #region Instance Action Handlers
        [ActionHandler("start")]
        internal async Task HandleActionStart(DuoInstance instance)
        {
            if (instance.IsRunning == false)
                await manager.Start(instance);
        }

        [ActionHandler("stop")]
        internal async Task HandleActionStop(DuoInstance instance)
        {
            if (instance.IsRunning == true)
                await manager.Stop(instance);
        }
        #endregion

        public override void Dispose()
        {
            manager.Started -= DuoService_Started;
            manager.Stopped -= DuoService_Stopped;

            base.Dispose();
        }
    }
}
