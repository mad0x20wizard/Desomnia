namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// How conflicting values from environment blocks of EQUAL priority are resolved
    /// (the conflictStrategy attribute); a block with higher priority always supersedes,
    /// regardless of this setting.
    /// </summary>
    internal enum ConflictResolution
    {
        /// <summary>The later block (in document order) wins, with a warning (the default).</summary>
        Last,

        /// <summary>The earlier block keeps its value, with a warning.</summary>
        First,

        /// <summary>Conflicting values abort startup.</summary>
        Error,
    }
}
