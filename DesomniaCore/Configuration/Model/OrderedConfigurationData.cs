namespace MadWizard.Desomnia.Configuration.Model
{
    /// <summary>
    /// Configuration provider data that remembers document order. The stock
    /// <c>ConfigurationProvider.GetChildKeys</c> sorts child keys with the
    /// <c>ConfigurationKeyComparer</c>, which would reorder collections ("Item#10" before
    /// "Item#2"); our providers keep the flattener's document order instead, so collection
    /// binding — and with it the ordered-DI world built on it — sees items exactly as they
    /// were written.
    /// </summary>
    internal sealed class OrderedConfigurationData
    {
        public static readonly OrderedConfigurationData Empty = new([]);

        readonly List<string> _orderedKeys = [];
        readonly List<KeyValuePair<string, string?>> _pairs = [];

        public OrderedConfigurationData(IEnumerable<KeyValuePair<string, string?>> pairs)
        {
            Dictionary<string, string?> data = new(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in pairs)
            {
                data.Add(pair.Key, pair.Value);

                _orderedKeys.Add(pair.Key);
                _pairs.Add(pair);
            }

            Data = data;
        }

        public IDictionary<string, string?> Data { get; }

        /// <summary>The key/value pairs in document order — the canonical form for change
        /// comparison and the flat effective-configuration export.</summary>
        public IReadOnlyList<KeyValuePair<string, string?>> Pairs => _pairs;

        /// <summary>
        /// The provider-side half of <c>IConfiguration.GetChildren()</c>: the distinct child
        /// segments below <paramref name="parentPath"/>, in document order, appended after the
        /// (already ordered) keys of earlier providers. The configuration root de-duplicates
        /// across providers.
        /// </summary>
        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            List<string> results = [.. earlierKeys];

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            string? prefix = parentPath is null ? null : parentPath + ":";

            foreach (var key in _orderedKeys)
            {
                string? segment = ChildSegment(key, prefix);

                if (segment is not null && seen.Add(segment))
                    results.Add(segment);
            }

            return results;
        }

        private static string? ChildSegment(string key, string? prefix)
        {
            int start = 0;

            if (prefix is not null)
            {
                if (key.Length <= prefix.Length || !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return null;

                start = prefix.Length;
            }

            int end = key.IndexOf(':', start);

            return end < 0 ? key[start..] : key[start..end];
        }
    }
}
