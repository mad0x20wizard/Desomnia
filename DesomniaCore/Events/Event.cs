namespace MadWizard.Desomnia.Events
{
    public class Event
    {
        private readonly ISet<object> _contexts = new HashSet<object>();

        public string Type { get => (field) ?? "unknown"; internal set; }

        /// <summary>
        /// The object this event (appears to have) originated from. Normally stamped by
        /// the trigger pipeline; publicly settable so that events can be dispatched on
        /// behalf of another source (see EventMetaObject.TryHandleEventAction).
        /// </summary>
        public EventMetaObject? Source { get; set; }

        // Parallel actions can share an event, but must keep their parent paths separate.
        private readonly AsyncLocal<Stack<EventMetaObject>> _parentContext = new();

        internal Stack<EventMetaObject> ParentContext
        {
            get => _parentContext.Value ??= [];
        }

        public IEnumerable<object> Context
        {
            get
            {
                yield return this;

                if (Source != null)
                    yield return Source;

                object[] snapshot;                // a delayed binding may enumerate this event on a
                lock (_contexts)                  // timer thread while a multi-owner re-trigger adds
                    snapshot = [.. _contexts];    // contexts concurrently

                foreach (var context in snapshot.Concat(ParentContext))
                {
                    yield return context;
                }
            }
        }

        public Event(string? type = null)
        {
            Type = type!;
        }

        public void AddContext(object context)
        {
            ArgumentNullException.ThrowIfNull(context);

            lock (_contexts)
                _contexts.Add(context);
        }

        public override string ToString() => $"{GetType().Name}('{Type}' at {Source?.GetType().Name ?? "???"})";
    }
}
