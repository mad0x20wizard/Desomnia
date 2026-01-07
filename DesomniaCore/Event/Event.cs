namespace MadWizard.Desomnia
{
    public class Event
    {
        private readonly ISet<object> _contexts = new HashSet<object>();

        public string Type { get => (field) ?? "unknown"; internal set; }

        public EventSource? Source { get; internal set; }

        public EventOptions Options { get; private init; }

        public IEnumerable<object> Context
        {
            get
            {
                yield return this;

                if (Source != null)
                    yield return Source;

                foreach (var context in _contexts)
                {
                    yield return context;
                }
            }
        }

        public Event(string? type = null, EventOptions options = default)
        {
            Type = type!;
            Options = options;
        }

        public void AddContext(object context)
        {
            ArgumentNullException.ThrowIfNull(context);

            _contexts.Add(context);
        }

        public override string ToString() => $"{GetType().Name}('{Type}' at {Source?.GetType().Name ?? "???"})";
    }
}
