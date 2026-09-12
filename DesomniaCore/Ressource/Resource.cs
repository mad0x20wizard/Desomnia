using MadWizard.Desomnia.Events;
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
    public abstract class Resource : EventMetaObject, IInspectable
    {
        /// <summary>The monitors tracking this resource — a view over the engine's
        /// parent edges (ServiceFilterWatch aggregates filter rules through it).</summary>
        protected IEnumerable<ResourceMonitor> Monitors => Parents.OfType<ResourceMonitor>();

        public bool IsMonitoredBy<T>() where T : ResourceMonitor => Parents.OfType<T>().Any();

        public bool IsIdle { get; private set; } = true;

        [EventOpposite(nameof(Demand))] // symmetric: either side's trigger aborts the other's pending action
        public event EventInvocation? Idle;
        public event EventInvocation? Demand;

        protected internal virtual void StartTrackingBy(ResourceMonitor monitor, bool adopt)
        {
            if (adopt)
            {
                AttachParent(monitor);
            }
        }

        private void TriggerIdle(Event @event)
        {
            IsIdle = true;

            Idle.TriggerEvent(@event); // opposite-cancel is pipeline-enforced — a VETOED event cancels nothing (§9.3)
        }

        protected void TriggerDemand(Event? eventObj = null)
        {
            TriggerDemandAsync(eventObj).Wait();
        }

        protected virtual async Task TriggerDemandAsync(Event? @event = null)
        {
            if (@event == null || @event.Type != nameof(Demand))
            {
                @event = new Event(nameof(Demand));
            }

            IsIdle = false;

            await Demand.TriggerEventAsync(@event);
        }

        public virtual IEnumerable<UsageToken> Inspect(TimeSpan interval) // TODO: maybe async?
        {
            Stopwatch watch = new();

            watch.Start();
            var tokens = InspectResource(interval).ToArray();
            watch.Stop();

            HandleInspectionResult(watch.Elapsed, tokens);

            return tokens;
        }

        protected abstract IEnumerable<UsageToken> InspectResource(TimeSpan interval);

        protected virtual void HandleInspectionResult(TimeSpan duration, IEnumerable<UsageToken> tokens)
        {
            if (tokens.Any())
            {
                TriggerDemand(new InspectionEvent(nameof(Demand)) { Duration = duration, Tokens = tokens });
            }
            else
            {
                TriggerIdle(new InspectionEvent(nameof(Idle)) { Duration = duration, Tokens = tokens });
            }
        }

        protected internal virtual void StopTrackingBy(ResourceMonitor monitor)
        {
            DetachParent(monitor);
        }
    }
}
