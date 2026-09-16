using MadWizard.Desomnia.Configuration.Model;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// The root settings of one parsed &lt;EnvironmentMonitor&gt; configuration generation,
    /// with the output paths already resolved to absolute paths (see the pipeline).
    /// </summary>
    /// <param name="Version">The configuration format version the generation is in - what the
    /// (possibly migrated) document declares, validated by the version check (see the pipeline);
    /// the effective XML declares it on its root element.</param>
    internal sealed record EnvironmentSettings(
        uint Version,
        TimeSpan Debounce,
        ConflictResolution OnConflict,
        string? WriteEffectiveXML,
        string? WriteEffectiveConfiguration);

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
