using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Events
{
    /// <summary>
    /// The language-level entry points into the event system. Inside the declaring class
    /// the event field itself is the handle — the engine's anchor delegate (seeded at
    /// index 0 of every invocation list, EventMetaObject ctor) glues the immutable
    /// delegate to its <see cref="EventType"/> meta object:
    /// <code>
    /// Usage.TriggerEvent();
    /// await Demand.TriggerEventAsync(demandEvent);
    /// Usage.AddAction(config.OnUsage);
    /// bool allowed = AllowSomething.Filter(true, someEvent);
    /// </code>
    /// </summary>
    public static class EventExtensions
    {
        extension(EventInvocation? source)
        {
            /// <summary>The event's meta object — the full engine surface.</summary>
            public EventType Meta
            {
                get
                {
                    if (source?.GetInvocationList() is [{ Target: EventType type }, ..])
                        return type;

                    throw new InvalidOperationException(
                        "delegate is not attached to the event system — only event members declared on an EventMetaObject carry a meta anchor");
                }
            }

            public bool HasHandlers => source.Meta.HasHandlers;

            /// <summary>Synchronous trigger — blocks like the legacy TriggerEvent.</summary>
            public void TriggerEvent(Event? @event = null) => source.Meta.TriggerEvent(@event);

            public Task TriggerEventAsync(Event? @event = null) => source.Meta.TriggerEventAsync(@event);

            public ActionBinding? AddAction(EventAction? action) => source.Meta.AddAction(action);

            /// <summary>Aborts this event's pending scheduled/throttled invocations.</summary>
            public void Cancel() => source.Meta.CancelActions();
        }

        extension<T>(EventFilter<T>? filter)
        {
            /// <summary>Folds the subscriber chain over the value in subscription order;
            /// identity when no subscriber is attached. Filter fields carry no anchor
            /// (AOT constraint) — the fold is entirely local.</summary>
            public T Filter(T value, Event? context = null)
            {
                if (filter is null)
                    return value;

                context ??= new Event();

                foreach (EventFilter<T> entry in filter.GetInvocationList())
                    value = entry(value, context);

                return value;
            }
        }

        extension<T, TEvent>(EventFilter<T, TEvent>? filter) where TEvent : Event
        {
            public T Filter(T value, TEvent context)
            {
                if (filter is null)
                    return value;

                foreach (EventFilter<T, TEvent> entry in filter.GetInvocationList())
                    value = entry(value, context);

                return value;
            }
        }
    }
}
