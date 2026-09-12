using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Ressource.Events;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Listener
{
    internal class SunshineListenerAdapter(DuoSessionMonitor monitor) : SunshineServiceAdapter, IDisposable
    {
        public required ILogger<SunshineListenerAdapter> Logger { get; set; }

        public required Func<SunshineService, SunshineListener> CreateSunshineListener { private get; init; }

        internal void Attach()
        {
            monitor.TrackingStarted += Monitor_TrackingStarted;
            monitor.TrackingStopped += Monitor_TrackingStopped;
        }

        private void Monitor_TrackingStarted(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            var instance = args.Inspectable;

            if (instance.Info.MinStreamTraffic != null)
                throw new FormatException("Cannot monitor MinStreamTraffic while in Listener Mode.");

            instance.Started += DuoInstance_Started;
            instance.Stopped += DuoInstance_Stopped;

            if (!instance.Settings.IsSandboxed)
            {
                Logger.LogInformation($"Monitoring {instance}:{instance.Settings.Port} -> using listener");

                RegisterWatch(instance, CreateSunshineListener(instance.Service));
            }
            else
            {
                Logger.LogWarning($"NOT Monitoring {instance}:{instance.Settings.Port} -> listener is not available for sandboxed instances");
            }
        }

        private async Task DuoInstance_Started(Event @event)
        {
            var instance = (DuoInstance)@event.Source!;

            foreach (var listener in instance.OfType<SunshineListener>())
                listener.StopWaiting();
        }

        private async Task DuoInstance_Stopped(Event @event)
        {
            var instance = (DuoInstance)@event.Source!;

            foreach (var listener in instance.OfType<SunshineListener>())
                listener.WaitForClient();
        }

        private void Monitor_TrackingStopped(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            var instance = args.Inspectable;

            instance.Started -= DuoInstance_Started;
            instance.Stopped -= DuoInstance_Stopped;

            foreach (var listener in instance.OfType<SunshineListener>())
            {
                UnregisterWatch(instance, listener);

                listener.Dispose();
            }
        }

        private void Detach()
        {
            monitor.TrackingStarted -= Monitor_TrackingStarted;
            monitor.TrackingStopped -= Monitor_TrackingStopped;
        }

        void IDisposable.Dispose()
        {
            Detach();
        }
    }
}
