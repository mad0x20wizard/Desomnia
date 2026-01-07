
using Autofac;

namespace MadWizard.Desomnia.Ressource
{
    public abstract class DynamicResourceMonitor<T> : ResourceMonitor<T>, IStartable, IDisposable where T : IInspectable
    {
        public required ILifetimeScope Scope { private get; init; }

        public virtual void Start()
        {
            foreach (var monitor in Scope.Resolve<IEnumerable<T>>())
            {
                this.StartTracking(monitor);
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            var resources = Scope.Resolve<IEnumerable<T>>();

            foreach (var monitor in this.ToList())
                if (!resources.Contains(monitor))
                    this.StopTracking(monitor);

            foreach (var monitor in resources)
                if (!this.Contains(monitor))
                    this.StartTracking(monitor);

            return base.InspectResource(interval);
        }

        public override void Dispose()
        {
            foreach (var monitor in this)
            {
                this.StopTracking(monitor);
            }

            base.Dispose();
        }

    }
}
