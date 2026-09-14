using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Ressource.Events;
using MadWizard.Desomnia.Session;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal abstract class BaseWatcher : IDuoInstanceWatcher
    {
        public required ILogger Logger { protected get; init; }

        public required IDuoManager Manager { protected get; init; }

        public event EventHandler<InstanceStatusChangedEventArgs>? StatusChanged;

        readonly HashSet<DuoInstance> _pendingStops = [];

        readonly Lock _statusLock = new();

        public abstract Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token);

        /// <summary>
        /// Helper method to refresh all instances using the Duo manager.
        /// </summary>
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
                lock (_statusLock)
                {
                    if (running)
                    {
                        CancelPendingStop(instance);
                    }

                    instance.IsRunning = running;

                    if (!running && DeferStopUntilSessionEnd(instance))
                    {
                        return; // publish status change later
                    }
                }

                PublishStatusChange(instance, running);
            }
        }

        #region Deferment of Stop Event 
        /// <summary>
        /// Unfortunately the DuoManager reports an instance as "stopped"
        /// as soon as the shutdown sequence has been started.
        /// 
        /// To make the workflow more predictable we postpone the stop event
        /// until the corresponding Windows session actually got terminated.
        /// </summary>
        /// 
        /// <returns>Did we schedule the event for later?</returns>
        private bool DeferStopUntilSessionEnd(DuoInstance instance)
        {
            _pendingStops.Add(instance);

            instance.TrackingStopped += Instance_TrackingStopped;

            if (!instance.TakeSnapshot().OfType<SessionWatch>().Any())
            {
                CancelPendingStop(instance);

                return false;
            }

            return true;
        }

        private void Instance_TrackingStopped(object? sender, InspectableEventArgs<Resource> args)
        {
            if (sender is DuoInstance instance && args.Inspectable is SessionWatch)
            {
                lock (_statusLock)
                {
                    if (!_pendingStops.Contains(instance) || instance.TakeSnapshot().OfType<SessionWatch>().Any())
                    {
                        return; // unrelated instance or session is still running
                    }

                    CancelPendingStop(instance);
                }

                PublishStatusChange(instance, false);
            }
        }

        private void CancelPendingStop(DuoInstance instance)
        {
            if (_pendingStops.Remove(instance))
            {
                instance.TrackingStopped -= Instance_TrackingStopped;
            }
        }
        #endregion

        private void PublishStatusChange(DuoInstance instance, bool running)
        {
            InstanceStatusChangedEventArgs args = new(instance, running);

            StatusChanged?.Invoke(this, args);

            Logger.LogInformation($"{args.Instance} is now {(args.Status ? "running" : "stopped")} " +
                $"{(args.Manually ? "(manually)" : "")}");

            ((IEventSystem)args.Instance)[args.Status ? nameof(DuoInstance.Started) : nameof(DuoInstance.Stopped)]
                .TriggerEventAsync();
        }
    }

    internal class InstanceStatusChangedEventArgs(DuoInstance instance, bool status) : EventArgs
    {
        public DuoInstance Instance { get; } = instance;

        public bool Manually { get; set; } = true;

        public bool Status { get; } = status;
    }
}
