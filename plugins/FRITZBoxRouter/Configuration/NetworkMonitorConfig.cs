namespace MadWizard.Desomnia.Network.FRITZ.Configuration
{
    /// <summary>
    /// The plugin's view of a &lt;NetworkMonitor&gt; element — the &lt;FRITZBoxRouter&gt; elements
    /// nested inside it, plus <c>autoDetect</c> (so the plugin can offer zero-conf discovery on
    /// networks that opted into <c>Router</c> without configuring a box). Name and everything else
    /// are bound (and validated) by the NetworkMonitor module; here it is just the container.
    /// </summary>
    public record class NetworkMonitorConfig : Network.Configuration.NetworkMonitorConfig
    {
        public IList<FRITZBoxRouterInfo> FRITZBoxRouter { get; private set; } = [];
    }
}
