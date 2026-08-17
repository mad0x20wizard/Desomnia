using MadWizard.Desomnia.Configuration.Model;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// The root settings of one parsed &lt;EnvironmentMonitor&gt; configuration generation,
    /// with the output paths already resolved to absolute paths (see the pipeline).
    /// </summary>
    internal sealed record EnvironmentSettings(
        string Version,
        TimeSpan Debounce,
        ConflictResolution OnConflict,
        string? OutputEffectiveXML,
        string? OutputEffectiveConfiguration);

    /// <summary>
    /// One computed effective configuration — the payload of
    /// <see cref="EnvironmentMonitor.EffectiveChanged"/>, consumed by the loosely coupled
    /// exporters and (as <see cref="Data"/>) served to the host by the monitor's
    /// configuration source.
    /// </summary>
    internal sealed record EffectiveConfiguration(
        EnvironmentSettings Settings,
        ConfigNode Root,
        OrderedConfigurationData Data,
        string ActiveDescription);
}
