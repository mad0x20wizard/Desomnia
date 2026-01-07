using MadWizard.Desomnia.Ressource.Events;

namespace MadWizard.Desomnia
{
    public abstract class ResourceMonitor : Resource { }

    public class ResourceMonitor<T> : ResourceMonitor, IIEnumerable<T> where T : IInspectable
    {
        public event Func<T, bool>? Filters;

        public event EventHandler<InspectableEventArgs<T>>? TrackingStarted;
        public event EventHandler<InspectableEventArgs<T>>? TrackingStopped;

        readonly HashSet<T> _inspectables = [];

        private bool ShouldTrackRessource(T inspectable)
        {
            if (Filters != null)
            {
                foreach (Func<T, bool> filter in Filters.GetInvocationList().Cast<Func<T, bool>>())
                    if (!filter(inspectable))
                        return false;
            }

            return true;
        }

        public virtual bool StartTracking(T inspectable, bool adopt = true)
        {
            if (ShouldTrackRessource(inspectable))
            {
                if (_inspectables.Add(inspectable))
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
            if (_inspectables.Remove(inspectable))
            {
                if (inspectable is Resource res)
                {
                    res.StopTrackingBy(this);

                    TrackingStopped?.Invoke(this, new InspectableEventArgs<T>(inspectable));
                }
            }
        }

        protected virtual bool ShouldInspectResource(T inspectable) => true;

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            foreach (var inspectable in this)
                if (ShouldInspectResource(inspectable))
                    foreach (var token in inspectable.Inspect(interval))
                        yield return token;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _inspectables.GetEnumerator();
    }
}
