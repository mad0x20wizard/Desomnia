namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// One configuration document under migration, as the file-format-agnostic
    /// <see cref="ConfigurationMigrator"/> sees it. A concrete format contributes an adapter (XML:
    /// <c>Configuration.Xml.XMigrationDocument</c>) that reads and stamps the version, reads the
    /// migration settings declared in the document, knows which modules can migrate ITS format
    /// and runs their steps — recording every change as a <see cref="MigrationAnnotation"/> —
    /// and writes the document back to its file. The engine owns the policy (which versions,
    /// in which order, what a warning means, when the file is written); the adapter owns the
    /// representation.
    /// </summary>
    internal interface IMigrationDocument
    {
        /// <summary>The format version the document declares (a document that declares none is in
        /// version 1); setting it stamps the new version into the document - into the format's
        /// authoritative place unconditionally, and into any alternative place the user chose,
        /// so the document keeps its style. An invalid or contradictory declaration is a
        /// <see cref="Binding.ConfigurationValueException"/>.</summary>
        uint Version { get; set; }

        /// <summary>The migration settings declared in the document (validated: a bad value is a
        /// <see cref="Binding.ConfigurationValueException"/>; a document that says nothing about
        /// migration gets <see cref="MigrationSettings.Default"/> - transient).</summary>
        MigrationSettings Settings { get; }

        /// <summary>The configuration file's path (the backup location resolves against it), or null when unknown.</summary>
        string? FilePath { get; }

        /// <summary>Whether the module can migrate THIS document's format, i.e. implements the
        /// format's migration interface — a module that requires a newer version without it
        /// makes an outdated file a hard error.</summary>
        bool Supports(ConfigurableModule module);

        /// <summary>Runs the module's step from <paramref name="version"/> - 1 to <paramref name="version"/>
        /// on the document, tracking every change into annotations. Only called for modules
        /// that <see cref="Supports"/> — the engine checks that before the first step.</summary>
        void Apply(ConfigurableModule module, uint version);

        /// <summary>The annotations recorded since the last call — the steps' notes and the
        /// tracked changes, in order — and empties the store.</summary>
        IReadOnlyList<MigrationAnnotation> TakeAnnotations();

        /// <summary>Writes the document to its file (a copy of the file goes to
        /// <paramref name="backupPath"/> first, when given), describing the step and its notes
        /// in the file for the user. Fails with an <see cref="IOException"/> or
        /// <see cref="UnauthorizedAccessException"/>, which the engine turns into the policy's
        /// write-failure branch.</summary>
        void Write(string? backupPath, uint sourceVersion, uint targetVersion, IReadOnlyList<MigrationAnnotation> notes);
    }
}
