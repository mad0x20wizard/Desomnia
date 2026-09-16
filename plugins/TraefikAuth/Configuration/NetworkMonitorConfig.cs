namespace MadWizard.Desomnia.Network.Traefik.Configuration
{
    /// <summary>
    /// The plugin's view of a &lt;NetworkMonitor&gt; element — extends the NetworkMonitor
    /// module's config, so it inherits the identity (<c>Ordinal</c>) that correlates this view
    /// with the module's, and sees the Traefik facets of the same elements: <c>traefikAuth…</c>
    /// options plus the &lt;RemoteHost&gt; items' &lt;HTTPService&gt; children (the hidden
    /// <see cref="RemoteHost"/> binds the same sections with the plugin's item type).
    /// </summary>
    public class NetworkMonitorConfig : Network.Configuration.NetworkMonitorConfig
    {
        internal ushort     TraefikAuthPort     { get; set; } = 5000;
        internal TimeSpan   TraefikAuthTimeout  { get; set; } = TimeSpan.FromSeconds(5);

        // Hosts
        public new IList<TraefikServerInfo> RemoteHost { get; private set; } = [];
    }
}
