using Autofac;
using MadWizard.Desomnia.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MadWizard.Desomnia
{
    public class ActionManager(IOptions<SystemMonitorConfig> config) : IStartable
    {
        public required ILogger<ActionManager> Logger { protected get; init; }

        private readonly List<Actor> _actors = [];

        public required IEnumerable<Actor> InjectableActors { private get; init; }

        void IStartable.Start()
        {
            //foreach (var group in config.Value.ActionGroup)
            //{
            //    RegisterActor(new ActionGroup(group));
            //}

            foreach (var actor in InjectableActors)
            {
                RegisterActor(actor);
            }
        }

        public void RegisterActor(Actor actor)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            _actors.Add(actor);
        }

        public async Task<bool> TryHandleEventAction(Event eventObj, NamedAction action)
        {
            foreach (var actor in _actors)
            {
                try
                {
                    if (await actor.TryHandleEventAction(eventObj, action))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    HandleActionError(new ActionError(eventObj, action, ex) { Actor = actor });

                    return true;
                }
            }

            return false;
        }

        public bool HandleActionError(ActionError error)
        {
            string postfix = ":";
            if (error.Actor != null && error.Event.Source != error.Actor)
                postfix = $" @ {error.Actor.GetType().Name}:";

            Logger.LogError(error.Exception, $"{error.Event} -> {error.Action}" + postfix);

            return true;
        }
    }
}
