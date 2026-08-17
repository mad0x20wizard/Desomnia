using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Configuration
{
    /// <summary>
    /// A configuration source that supports persistent (process-lifetime) configuration:
    /// key/value entries read once, before any container is built, and handed to every
    /// module's <c>LoadOnce(builder, config)</c> — the way to configure services of the
    /// persistent container, which exists before (and independently of) any effective
    /// application configuration. In the XML representation these are the
    /// <c>&lt;?global key="value"?&gt;</c> processing instructions outside the root element.
    ///
    /// <para>Persistent configuration is a completely optional feature: a source that does
    /// not implement this interface simply yields an empty persistent configuration. Because
    /// the persistent container cannot be rebuilt, a change to these entries is fatal — the
    /// application exits with an error code and relies on the service manager to restart it.</para>
    /// </summary>
    public interface IPersistentConfigurationSource : IConfigurationSource
    {
        /// <summary>
        /// Reads the persistent configuration entries from the physical source. Keys are full
        /// configuration paths (e.g. "ProcessManager:pollInterval"). Returns an empty sequence
        /// when the source (or the file) has none.
        /// </summary>
        IEnumerable<KeyValuePair<string, string>> LoadPersistentConfiguration();
    }
}
