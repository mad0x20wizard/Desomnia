namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// What an outdated configuration file may be subjected to (the <c>autoMigrate</c> of the
    /// file's <c>&lt;?config?&gt;</c> header, see <see cref="MigrationSettings"/>): nothing at all
    /// (the file must be current), a migration in memory only, and/or writing the migrated
    /// document back to the file.
    /// </summary>
    [Flags]
    public enum MigrationOption
    {
        /// <summary>An outdated file is refused (<c>autoMigrate="never"</c>).</summary>
        None = 0,

        /// <summary>The document is migrated in memory on every read; the file stays as it is.</summary>
        Transient = 1,

        /// <summary>The file is rewritten after every warning-free migration step (<c>autoMigrate="persistent"</c>).</summary>
        Persistent = 2,
    }
}
