using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal abstract class BaseWatcher : IDuoSessionWatcher
    {
        protected bool HasInitialized { get; private set; } = false;

        public required ILogger Logger { protected get; init; }

        public required ISessionManager SessionManager { protected get; init; }

        public event EventHandler<InstanceSessionChangedEventArgs>? SessionChanged;

        public virtual async Task StartWatch(IEnumerable<DuoInstance> instances, CancellationToken token)
        {
            HasInitialized = true;
        }

        protected virtual void NotifySessionChange(DuoInstance instance, ISession? session)
        {
            InstanceSessionChangedEventArgs args = new(instance, session);

            SessionChanged?.Invoke(this, args);

            if (HasInitialized)
            {
                Logger.LogInformation($"{args.Instance} is now " +
                    $"{(args.IsRunning ? "running" : "stopped")} " +
                    $"{(args.Manually ? "(manually)" : "")}");

                ((IEventSystem)args.Instance)[args.IsRunning 
                    ? nameof(DuoInstance.Started) 
                    : nameof(DuoInstance.Stopped)]
                        .TriggerEventAsync();
            }
        }

        public virtual void StopWatch()
        {
            HasInitialized = false;
        }
    }
}
