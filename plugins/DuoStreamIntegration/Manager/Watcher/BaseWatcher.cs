using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal abstract class BaseWatcher : IDuoSessionWatcher
    {
        bool _initialized = false;

        public required ILogger Logger { protected get; init; }

        public event EventHandler<InstanceSessionChangedEventArgs>? SessionChanged;

        public virtual async Task StartWatch(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            _initialized = true;
        }

        protected void PublishSessionChange(DuoInstance instance, ISession? session)
        {
            InstanceSessionChangedEventArgs args = new(instance, session);

            SessionChanged?.Invoke(this, args);

            if (_initialized)
            {
                Logger.LogInformation($"{args.Instance} is now {(args.IsRunning ? "running" : "stopped")} " +
                    $"{(args.Manually ? "(manually)" : "")}");

                ((IEventSystem)args.Instance)[args.IsRunning ? nameof(DuoInstance.Started) : nameof(DuoInstance.Stopped)]
                    .TriggerEventAsync();
            }
        }

        public abstract void StopWatch();
    }
}
