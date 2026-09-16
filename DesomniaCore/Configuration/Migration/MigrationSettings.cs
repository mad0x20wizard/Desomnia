using MadWizard.Desomnia.Configuration.Binding;

namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// The migration settings a configuration file declares about itself — a property of the
    /// file, not of the configuration: the format adapter reads the raw values (XML: the
    /// <c>&lt;?config autoMigrate="..." writeBackupXML="..."?&gt;</c> header, the first
    /// processing instruction of the file), this record validates and interprets them, format
    /// independent. A file that says nothing about migration — no header at all, or a header
    /// without <c>autoMigrate</c> — is migrated in memory (<see cref="Default"/>): the header
    /// only needs writing to FORBID the migration or make it persistent.
    /// </summary>
    /// <param name="Option">What an outdated file may be subjected to.</param>
    /// <param name="BackupPattern">The backup pattern as written (unresolved), or null.</param>
    internal sealed record MigrationSettings(MigrationOption Option, string? BackupPattern)
    {
        public const string AUTO_MIGRATE_KEY = "autoMigrate";
        public const string WRITE_BACKUP_KEY = "writeBackupXML";

        /// <summary>The placeholder in <see cref="BackupPattern"/> that stands for the version of the content being backed up.</summary>
        public const char VERSION_PLACEHOLDER = '?';

        const string NEVER_KEYWORD = "never";
        const string NONE_KEYWORD = "none"; // accepted alias of "never"
        const string TRANSIENT_KEYWORD = "transient";
        const string PERSISTENT_KEYWORD = "persistent";

        /// <summary>The settings of a file that says nothing about migration: migrated in memory, never written.</summary>
        public static readonly MigrationSettings Default = new(MigrationOption.Transient, null);

        /// <summary>
        /// Builds the settings of a declared header from its values (null = not declared); a
        /// header that does not say <c>autoMigrate</c> keeps the default ("transient") — only
        /// "never" and "persistent" need declaring.
        /// <paramref name="configPath"/> (when known) lets the backup pattern be checked against
        /// the configuration file right away, so a self-overwriting pattern is refused before
        /// any migration runs.
        /// </summary>
        public static MigrationSettings Create(string? autoMigrate, string? writeBackup, string? configPath)
        {
            var option = autoMigrate is null ? MigrationOption.Transient : ParseOption(autoMigrate);

            if (writeBackup is not null && string.IsNullOrWhiteSpace(writeBackup))
                throw new ConfigurationValueException($"{WRITE_BACKUP_KEY} requires a path.");

            var settings = new MigrationSettings(option, writeBackup);

            if (configPath is not null)
                settings.ResolveBackupPath(1, configPath); // fail early on a self-overwriting pattern

            return settings;
        }

        /// <summary>
        /// Parses the pipe-separated <c>autoMigrate</c> flags: <c>never</c> (or <c>none</c>)
        /// stands alone, <c>transient</c> and <c>persistent</c> combine. Hand-rolled instead of the
        /// generic flag normalization because of the "never combines with nothing" rule.
        /// </summary>
        internal static MigrationOption ParseOption(string value)
        {
            var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) // empty, whitespace, or separators only ("|"): no keyword at all
                throw new ConfigurationValueException($"{AUTO_MIGRATE_KEY} must not be empty; " +
                    $"expected \"{NEVER_KEYWORD}\", \"{TRANSIENT_KEYWORD}\", \"{PERSISTENT_KEYWORD}\" or \"{TRANSIENT_KEYWORD}|{PERSISTENT_KEYWORD}\".");

            var option = MigrationOption.None;
            bool never = false;

            foreach (var part in parts)
            {
                if (part.Equals(NEVER_KEYWORD, StringComparison.OrdinalIgnoreCase) || part.Equals(NONE_KEYWORD, StringComparison.OrdinalIgnoreCase))
                    never = true;
                else if (part.Equals(TRANSIENT_KEYWORD, StringComparison.OrdinalIgnoreCase))
                    option |= MigrationOption.Transient;
                else if (part.Equals(PERSISTENT_KEYWORD, StringComparison.OrdinalIgnoreCase))
                    option |= MigrationOption.Persistent;
                else
                    throw new ConfigurationValueException($"Invalid {AUTO_MIGRATE_KEY} = \"{value}\"; " +
                        $"expected \"{NEVER_KEYWORD}\", \"{TRANSIENT_KEYWORD}\", \"{PERSISTENT_KEYWORD}\" or \"{TRANSIENT_KEYWORD}|{PERSISTENT_KEYWORD}\".");
            }

            if (never && (option != MigrationOption.None || parts.Length > 1))
                throw new ConfigurationValueException($"{AUTO_MIGRATE_KEY} = \"{NEVER_KEYWORD}\" cannot be combined with other options.");

            return option;
        }

        /// <summary>
        /// The backup file for the content of format version <paramref name="version"/>: the
        /// pattern with every placeholder replaced, resolved relative to the configuration file
        /// (unless absolute). Null when no backup is configured.
        /// </summary>
        /// <exception cref="ConfigurationValueException">The pattern resolves to the configuration file itself.</exception>
        public string? ResolveBackupPath(uint version, string configPath)
        {
            if (BackupPattern is null)
                return null;

            string configFullPath = Path.GetFullPath(configPath);

            string backupPath = Path.GetFullPath(BackupPattern.Replace(VERSION_PLACEHOLDER.ToString(), version.ToString()),
                Path.GetDirectoryName(configFullPath)!);

            // the backup is taken right before the file is overwritten - pointing it at the file
            // would copy the file onto itself and lose the original
            if (string.Equals(backupPath, configFullPath, StringComparison.OrdinalIgnoreCase))
                throw new ConfigurationValueException($"{WRITE_BACKUP_KEY} must not point at the configuration file itself ({configFullPath}).");

            return backupPath;
        }
    }
}
