namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// The configuration file cannot be brought to a format version this build supports: it is
    /// newer than the build, the registered modules disagree on the versions, migration is
    /// disabled, or a migration step failed or needs manual attention. Deliberately NOT a
    /// <see cref="Binding.ConfigurationValueException"/> — that one stands for a bad value in
    /// the file, this one for a format-level problem (the details are always logged first).
    /// </summary>
    public class ConfigurationMigrationException : Exception
    {
        public ConfigurationMigrationException(string message) : base(message) { }

        public ConfigurationMigrationException(string message, Exception? innerException) : base(message, innerException) { }
    }
}
