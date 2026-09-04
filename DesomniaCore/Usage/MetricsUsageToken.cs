using System.Collections;

namespace MadWizard.Desomnia
{
    public abstract class MetricsUsageToken<T> : UsageToken where T : MetricsUsage?
    {
        public required T Metrics { get; init; }
    }

    public class MetricsUsage : IReadOnlyDictionary<string, bool>
    {
        private readonly Dictionary<string, bool> _values = new(StringComparer.OrdinalIgnoreCase);

        public bool this[string key] => _values[key];
        public IEnumerable<string> Keys => _values.Keys;
        public IEnumerable<bool> Values => _values.Values;
        public int Count => _values.Count;

        public void Add(string name, bool value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!_values.TryAdd(name, value))
                throw new InvalidOperationException($"Metric '{name}' was supplied more than once.");
        }

        public void AddRange(IEnumerable<KeyValuePair<string, bool>> metrics)
        {
            foreach (var (name, value) in metrics)
                Add(name, value);
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public bool TryGetValue(string key, out bool value) => _values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, bool>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
