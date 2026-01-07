using System.Reflection;

namespace MadWizard.Desomnia
{
    public abstract class EventSource
    {
        private readonly Dictionary<string, EventType> _events = [];

        protected EventSource()
        {
            foreach (var eventInfo in GetType().GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (eventInfo.EventHandlerType?.Name.Contains("EventInvocation") ?? false)
                {
                    var fieldInfo = GetType().GetAllFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.Name == eventInfo.Name).First();

                    if (fieldInfo != null)
                    {
                        EventType type = new(eventInfo, fieldInfo);

                        _events.Add(eventInfo.Name, type);
                    }
                }
            }
        }

        protected EventType EventTypeByName(string eventName)
        {
            return _events[eventName];
        }

        #region Event handler management
        public void AddEventHandler(string eventName, EventInvocation handler)
        {
            _events[eventName].AddEventHandler(this, handler);
        }

        public bool HasEventHandlers(string eventName)
        {
            return _events[eventName].HasAnyEventHandler(this);
        }

        public void RemoveEventHandler(string eventName, EventInvocation handler)
        {
            _events[eventName].RemoveEventHandler(this, handler);
        }
        #endregion

        #region Trigger methods
        protected void TriggerEvent(string eventName)
        {
            TriggerEventAsync(eventName).Wait();
        }

        protected void TriggerEvent(string eventName, Event @event)
        {
            TriggerEventAsync(eventName, @event).Wait();
        }

        protected void TriggerEvent(Event @event)
        {
            TriggerEventAsync(@event).Wait();
        }

        protected async Task TriggerEventAsync(string eventName)
        {
            await TriggerEventAsync(new Event(eventName));
        }

        protected async Task TriggerEventAsync(string eventName, Event @event)
        {
            @event.Type = eventName;

            await TriggerEventAsync(@event);
        }

        protected virtual async Task TriggerEventAsync(Event @event)
        {
            if (_events.TryGetValue(@event.Type, out var type))
            {
                @event.Source = this;

                foreach (var context in Context)
                {
                    @event.AddContext(context);
                }

                foreach (var handler in type.GetInvocationList(this).Cast<EventInvocation>())
                {
                    await handler(@event);
                }
            }
        }
        #endregion


        private IEnumerable<object> Context
        {
            get
            {
                foreach (var property in GetType().GetAllProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.GetCustomAttribute<EventContextAttribute>() != null))
                {
                    var context = property.GetValue(this);

                    if (context != null)
                    {
                        yield return context;
                    }
                }
            }
        }
    }
}
