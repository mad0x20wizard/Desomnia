using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Ressource.Events;

namespace MadWizard.Desomnia
{
    public abstract class ResourceMonitor : Resource { }

    public class ResourceMonitor<T> : ResourceMonitor, IIEnumerable<T> where T : IInspectable
    {
        public event Func<T, bool>? TrackingFilter;
        public event Func<T, bool>? InspectionFilter;

        public event EventHandler<InspectableEventArgs<T>>? TrackingStarted;
        public event EventHandler<InspectableEventArgs<T>>? TrackingStopped;

        public event EventHandler<TimeSpan>? InspectResources;

        // mutated on observer threads (explicit hand-off, §7.2) while the inspection
        // loop enumerates — guarded, with snapshot-on-enumerate
        private readonly HashSet<T> _inspectables = [];

        private bool ShouldTrackRessource(T inspectable)
        {
            if (TrackingFilter != null)
            {
                foreach (Func<T, bool> filter in TrackingFilter.GetInvocationList().Cast<Func<T, bool>>())
                    if (!filter(inspectable))
                        return false;
            }

            return true;
        }

        public virtual bool StartTracking(T inspectable, bool adopt = true)
        {
            if (inspectable is EventMetaObject { IsEngineDisposed: true })
                return false; // never adopt a corpse

            if (ShouldTrackRessource(inspectable))
            {
                bool added;

                lock (_inspectables)
                {
                    added = _inspectables.Add(inspectable);
                }

                if (added)
                {
                    if (inspectable is Resource res)
                    {
                        res.StartTrackingBy(this, adopt);
                    }

                    TrackingStarted?.Invoke(this, new InspectableEventArgs<T>(inspectable));

                    return true;
                }

                return false;
            }

            return false;
        }

        public virtual void StopTracking(T inspectable)
        {
            bool removed;

            lock (_inspectables)
            {
                removed = _inspectables.Remove(inspectable);
            }

            if (removed)
            {
                if (inspectable is Resource res)
                {
                    res.StopTrackingBy(this);
                }

                TrackingStopped?.Invoke(this, new InspectableEventArgs<T>(inspectable));
            }
        }

        protected virtual bool ShouldInspectResource(T inspectable)
        {
            if (InspectionFilter != null)
            {
                foreach (Func<T, bool> filter in InspectionFilter.GetInvocationList().Cast<Func<T, bool>>())
                    if (!filter(inspectable))
                        return false;
            }

            return true;
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            InspectResources?.Invoke(this, interval);

            foreach (var inspectable in TakeSnapshot())
                if (ShouldInspectResource(inspectable))
                {
                    foreach (var token in InspectResource(inspectable, interval))
                        yield return token;
                }
        }

        protected virtual IEnumerable<UsageToken> InspectResource(T inspectable, TimeSpan interval)
        {
            return inspectable.Inspect(interval);
        }

        public override void Dispose()
        {
            foreach (var inspectable in TakeSnapshot())
            {
                this.StopTracking(inspectable);
            }

            base.Dispose();
        }

        internal T[] TakeSnapshot()
        {
            lock (_inspectables)
            {
                // roster disposal backstop (§7.1): members disposed without an explicit
                // StopTracking (crash paths) are evicted lazily — a dead monitor must
                // never be inspected again (its edges were already dropped by Dispose)
                _inspectables.RemoveWhere(i => i is EventMetaObject { IsEngineDisposed: true });

                return [.. _inspectables];
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _inspectables.GetEnumerator();
    }
}
