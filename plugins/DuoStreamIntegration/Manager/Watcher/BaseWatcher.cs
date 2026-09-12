using MadWizard.Desomnia.Events;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal abstract class BaseWatcher : IDuoInstanceWatcher
    {
        public required ILogger Logger { protected get; init; }

        public required IDuoManager Manager { protected get; init; }

        public event EventHandler<InstanceStatusChangedEventArgs>? StatusChanged;

        public abstract Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken stoppingToken);

        protected async Task RefreshInstances(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            foreach (var instance in instances)
            {
                bool status = await Manager.QueryRunningState(instance, token);

                token.ThrowIfCancellationRequested();

                NotifyInstanceStatus(instance, status);
            }
        }

        protected void NotifyInstanceStatus(DuoInstance instance, bool running)
        {
            if (instance.IsRunning != running)
            {
                InstanceStatusChangedEventArgs args = new(instance, running);

                instance.IsRunning = running;

                StatusChanged?.Invoke(this, args);

                Logger.LogInformation($"{instance} is now {(running ? "running" : "stopped")} " +
                    $"{(args.Manually ? "(manually)" : "")}");

                ((IEventSystem)instance)[running ? nameof(DuoInstance.Started) : nameof(DuoInstance.Stopped)]
                    .TriggerEventAsync();
            }
        }
    }

    internal class InstanceStatusChangedEventArgs(DuoInstance instance, bool status) : EventArgs
    {
        public DuoInstance Instance { get; } = instance;

        public bool Manually { get; set; } = true;

        public bool Status { get; } = status;
    }
}
