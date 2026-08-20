using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// A <see cref="ConfigurableModule"/> that can migrate an XML configuration file to a newer
    /// format version. Whether a module supports migration at all is decided by this interface
    /// (the version facts — <see cref="ConfigurableModule.MinVersion"/>, <see cref="ConfigurableModule.MaxVersion"/>
    /// — stay on the module): a module that raises its <see cref="ConfigurableModule.MinVersion"/>
    /// without implementing it turns every outdated file into a hard startup error.
    /// </summary>
    public interface IXConfigurationMigration
    {
        /// <summary>
        /// Migrates the configuration document IN PLACE from format version <paramref name="version"/> - 1
        /// to <paramref name="version"/>: called once per version step, for every step between the file's
        /// version and the supported one, but only while <see cref="ConfigurableModule.MinVersion"/> >= version — a
        /// module switches over the version to pick the transformation(s) of that step (usually there is exactly
        /// one). Everything runs before any framework transformation, on the raw XML: in augmenting mode
        /// the root is &lt;EnvironmentMonitor&gt; and the module's elements appear once per environment block.
        /// Make every change through the helpers of <see cref="XMigrationExtensions"/> (<c>MigrateRename</c>,
        /// <c>MigrateSetValue</c>, <c>MigrateRemove</c>, ..., or the general <c>Migrate</c> with an action) —
        /// they record the change with the node's path as the user's file has it; a change made past them is
        /// tracked and reported as a Warning. A note above Warning aborts the startup; throw
        /// <see cref="Binding.ConfigurationValueException"/> for a hard configuration error (also aborts).
        /// </summary>
        void Run(XDocument configuration, uint version);
    }
}
