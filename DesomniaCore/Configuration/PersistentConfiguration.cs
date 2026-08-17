using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Configuration
{
    /// <summary>
    /// The persistent (process-lifetime) configuration: the immutable snapshot of the
    /// source's <see cref="IPersistentConfigurationSource"/> entries taken at boot, exposed
    /// as a standard <see cref="IConfiguration"/> for the modules' <c>LoadOnce</c>. Because
    /// the persistent container cannot be rebuilt, the snapshot doubles as the change
    /// baseline: when a configuration reload yields entries that no longer
    /// <see cref="Matches"/> this snapshot, the application must exit and restart.
    /// </summary>
    internal sealed class PersistentConfiguration
    {
        public static readonly PersistentConfiguration Empty = new([]);

        /// <summary>Reads the source's persistent entries (empty for sources without
        /// persistent-configuration support).</summary>
        public static PersistentConfiguration LoadFrom(IConfigurationSource source)
            => source is IPersistentConfigurationSource persistent
                ? new PersistentConfiguration(persistent.LoadPersistentConfiguration())
                : Empty;

        readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

        public PersistentConfiguration(IEnumerable<KeyValuePair<string, string>> entries)
        {
            foreach (var (key, value) in entries)
            {
                if (!_entries.TryAdd(key, value))
                    throw new ConfigurationValueException($"Duplicate persistent configuration key '{key}'.");
            }

            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(_entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
                .Build();
        }

        /// <summary>The entries as a standard, strict-binder-bindable configuration.</summary>
        public IConfiguration Configuration { get; }

        public bool IsEmpty => _entries.Count == 0;

        /// <summary>Whether the given entries are equivalent to this snapshot (keys
        /// case-insensitive, values exact) - false means the persistent configuration
        /// changed and the application must restart to apply it.</summary>
        public bool Matches(IEnumerable<KeyValuePair<string, string>> entries)
        {
            int count = 0;

            foreach (var (key, value) in entries)
            {
                if (!_entries.TryGetValue(key, out var existing) || !string.Equals(existing, value, StringComparison.Ordinal))
                    return false;

                count++;
            }

            return count == _entries.Count;
        }
    }
}
