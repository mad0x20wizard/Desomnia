using Microsoft.Extensions.Configuration;

namespace MadWizard.Desomnia.Configuration
{
    /// <summary>
    /// A configuration source that carries a root configuration: the entries meant for the
    /// root (process-lifetime) host — the host whose intrinsic container is the persistent
    /// scope — exposed as a nested <see cref="IConfigurationSource"/> that the root host's
    /// configuration consumes like any other source. The modules read it through the host
    /// builder's configuration in <c>BuildOnce</c>/<c>LoadOnce</c>, and the persistent
    /// services bind it through the standard options interfaces (<c>IOptions&lt;T&gt;</c>,
    /// <c>IOptionsMonitor&lt;T&gt;</c>). In the XML representation these are the
    /// <c>&lt;?global key="value"?&gt;</c> processing instructions outside the root element;
    /// the interface itself makes no assumption about the physical representation.
    ///
    /// <para>Root configuration is a completely optional feature: a source that does not
    /// implement this interface simply gives the root host an empty configuration. Whether a
    /// change of the entries reaches the running process is up to the nested source (a
    /// file-based one reloads along with its parent's <c>ReloadOnChange</c>) and to the
    /// consumer: an <c>IOptionsMonitor&lt;T&gt;</c> follows it, a value bound while the
    /// container was built keeps its boot-time value — a change is never an error.</para>
    /// </summary>
    public interface IRootConfigurationSource : IConfigurationSource
    {
        /// <summary>
        /// The nested source carrying the root configuration entries. Keys are full
        /// configuration paths (e.g. "ProcessManager:pollInterval"); a source (or file)
        /// without entries yields an empty configuration.
        /// </summary>
        IConfigurationSource RootSource { get; }
    }
}
