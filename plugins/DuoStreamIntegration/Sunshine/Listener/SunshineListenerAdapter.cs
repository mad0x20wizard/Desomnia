using Autofac;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Listener
{
    internal class SunshineListenerAdapter(DuoManager manager) : SunshineServiceAdapter, IStartable, IDisposable
    {
        public required ILogger<SunshineListenerAdapter> Logger { get; set; }

        public required Func<SunshineService, SunshineListener> CreateSunshineListener { private get; init; }

        void IStartable.Start()
        {
            manager.Started += DuoService_Started;
            manager.Stopped += DuoService_Stopped;
        }

        private void DuoService_Started(object? sender, DuoLifecycleEventArgs args)
        {
            foreach (var instance in args.Instances)
            {
                if (instance.Info.MinStreamTraffic != null)
                    throw new FormatException("Cannot monitor MinStreamTraffic while in Listener Mode.");

                instance.Started += DuoInstance_Started;
                instance.Stopped += DuoInstance_Stopped;

                if (!instance.IsSandboxed)
                {
                    Logger.LogInformation($"Monitoring {instance}:{instance.Port} -> using fallback");

                    RegisterWatch(instance, CreateSunshineListener(instance.Service));
                }
                else
                {
                    Logger.LogWarning($"NOT Monitoring {instance}:{instance.Port} -> fallback is not available for sandboxed instances");

                    continue;
                }
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

        private void DuoService_Stopped(object? sender, DuoLifecycleEventArgs args)
        {
            foreach (var instance in args.Instances)
            {
                instance.Started -= DuoInstance_Started;
                instance.Stopped -= DuoInstance_Stopped;

                foreach (var listener in instance.OfType<SunshineListener>())
                {
                    UnregisterWatch(instance, listener);

                    listener.Dispose();
                }
            }
        }

        void IDisposable.Dispose()
        {
            manager.Started -= DuoService_Started;
            manager.Stopped -= DuoService_Stopped;
        }
    }
}
