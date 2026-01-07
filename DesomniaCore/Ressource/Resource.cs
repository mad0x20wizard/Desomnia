using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Ressource.Events;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia
{
    /**
     * Base class for a ressource that can be monitored for usage. A ressource is an entity, that must be identifiable unambiguously.
     * 
     * Every ressource can be configured with an idle action that is triggered when it is detected, that the ressource is no longer in use.
     * 
     * Actions can be triggered manually or scheduled to be triggered after a certain delay.
     */
    public abstract class Resource : Actor, IInspectable
    {
        readonly ISet<ResourceMonitor> Monitors = new HashSet<ResourceMonitor>();

        //public bool IsTrackedBy(object monitor) => Monitors.Contains(monitor);
        //public bool IsTrackedBy<T>() => Monitors.OfType<T>().Any();

        public bool IsIdle { get; private set; } = true;

        public event EventInvocation? Idle;
        public event EventInvocation? Demand;

        protected event EventHandler<MonitorEventArgs>? MonitoringStarted;
        protected event EventHandler<MonitorEventArgs>? MonitoringStopped;

        internal void StartTrackingBy(ResourceMonitor monitor, bool adopt)
        {
            if (adopt)
            {
                Monitors.Add(monitor);
            }

            MonitoringStarted?.Invoke(this, new MonitorEventArgs(monitor));
        }

        private void TriggerIdle(Event @event)
        {
            CancelEventAction(nameof(Demand));

            IsIdle = true;

            TriggerEvent(@event);
        }

        protected void TriggerDemand(Event? eventObj = null)
        {
            TriggerDemandAsync(eventObj).Wait();
        }

        protected virtual async Task TriggerDemandAsync(Event? @event = null)
        {
            CancelEventAction(nameof(Idle));

            if (@event == null || @event.Type != nameof(Demand))
            {
                @event = new Event(nameof(Demand));
            }

            IsIdle = false;

            await TriggerEventAsync(@event);
        }

        public virtual IEnumerable<UsageToken> Inspect(TimeSpan interval) // TODO: maybe async?
        {
            Stopwatch watch = new();

            watch.Start();
            var tokens = InspectResource(interval).ToArray();
            watch.Stop();

            if (tokens.Length == 0)
            {
                TriggerIdle(new InspectionEvent(nameof(Idle)) { Duration = watch.Elapsed, Tokens = tokens });
            }
            else
            {
                TriggerDemand(new InspectionEvent(nameof(Demand)) { Duration = watch.Elapsed, Tokens = tokens });
            }

            return tokens;
        }

        protected abstract IEnumerable<UsageToken> InspectResource(TimeSpan interval);

        internal void StopTrackingBy(ResourceMonitor monitor)
        {
            Monitors.Remove(monitor);

            MonitoringStopped?.Invoke(this, new MonitorEventArgs(monitor));
        }

        #region Action/Error-Bubbling
        protected override async Task<bool> HandleEventAction(Event eventObj, NamedAction action)
        {
            if (!await base.HandleEventAction(eventObj, action))
            {
                foreach (var monitor in Monitors)
                    if (await monitor.HandleEventAction(eventObj, action))
                        return true;

                return false;
            }

            return true;
        }

        protected override bool HandleActionError(ActionError error)
        {
            if (!base.HandleActionError(error))
            {
                foreach (var monitor in Monitors)
                    if (monitor.HandleActionError(error))
                        return true;
            }

            return false;
        }
        #endregion
    }
}
