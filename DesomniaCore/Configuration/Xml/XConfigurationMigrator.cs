using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The XML front of the migration engine — a self-contained layer, attached UNDER the file
    /// source by decorating its file provider (see <see cref="MigratingFileProvider"/>), never
    /// part of the source itself: every read of the file passes through <see cref="Migrate"/>,
    /// whose <see cref="Read(string)"/> parses the text ONCE per distinct text, hands it to the
    /// format-agnostic <see cref="ConfigurationMigrator"/> behind an <see cref="XMigrationDocument"/>
    /// and caches the result — so however many consumers read the provider (the content
    /// provider, the root provider, the configuration pipeline), one file text is one parse,
    /// one migration, one set of log lines, and everything downstream only ever sees the
    /// migrated form without knowing migration exists.
    /// </summary>
    internal sealed class XConfigurationMigrator : IConfigurationFileMigrator
    {
        readonly Func<string?> _filePath;

        // one lock for the cache AND the migration (including its file write): a
        // watcher-triggered reload that arrives while a migration is still writing the file
        // waits until the write is done
        readonly Lock _lock = new();

        // single-entry cache: the three readers of one file text share the parse and the
        // migration (and the log lines), keyed by the exact text - and, when that migration
        // wrote the file, only as long as the file does not hold the key text again (see Read)
        string? _cachedText;
        XmlConfigurationFile? _cachedFile;
        bool _cachedWritten;
        bool _cachedMigrated;
        byte[]? _cachedServed; // the rendered migrated form, once a byte reader asked for it

        // the texts the migration wrote to the file during the current run (one per step - see
        // IsOwnWrite); retired as soon as a foreign text is read
        readonly HashSet<string> _ownTexts = [];

        /// <param name="registry">The module registry whose format algebra decides what to migrate.</param>
        /// <param name="filePath">The configuration file's full path — evaluated lazily, because a
        /// relative source path resolves its file provider only when the configuration is built.</param>
        public XConfigurationMigrator(VersionedModuleRegistry registry, Func<string?> filePath)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(filePath);

            _filePath = filePath;

            Engine = new ConfigurationMigrator(registry);
        }

        /// <summary>The standalone convenience constructor (tests): a migrator over its own registry.</summary>
        public XConfigurationMigrator(Func<string?> filePath) : this(new VersionedModuleRegistry(), filePath) { }

        /// <summary>The format-agnostic engine: migration steps, policy, logging.</summary>
        internal ConfigurationMigrator Engine { get; }

        /// <summary>The module registry the engine works against (format algebra and version check).</summary>
        internal VersionedModuleRegistry Registry => Engine.Registry;

        /// <summary>The migration log (see <see cref="ConfigurationMigrator.Logger"/> - the engine's own category).</summary>
        internal ILogger Logger
        {
            get => Engine.Logger;
            set => Engine.Logger = value;
        }

        /// <summary>The newest format version this build knows. Test seam (see <see cref="ModuleRegistry.LatestVersion"/>).</summary>
        internal uint LatestVersion
        {
            get => Registry.LatestVersion;
            init => Registry.LatestVersion = value;
        }

        internal uint SupportedVersion => Registry.SupportedVersion;

        internal uint RequiredVersion => Registry.RequiredVersion;

        /// <summary>Registers a module with the registry (before the first read).</summary>
        internal void Register(ConfigurableModule module) => Registry.Register(module);

        /// <summary>Whether the text is one the migration wrote to the file during the current
        /// run — the file watcher's report of such a write is not a configuration change (the
        /// data is identical by construction). Only relative to the file history: once a
        /// different (foreign) text has been read, the marker is retired, so a later edit that
        /// happens to restore the written text counts as the change it is.</summary>
        internal bool IsOwnWrite(string text)
        {
            lock (_lock) return _ownTexts.Contains(text);
        }

        /// <summary>
        /// The byte face of the cache, for the file provider: decodes and reads the content
        /// (migrating it when a loaded module demands a newer version, see <see cref="Read(string)"/>)
        /// and returns the content to serve — the input itself when the document was not
        /// migrated (byte-for-byte the file's content), the rendered migrated document otherwise
        /// (what a persistent migration would write, minus the protocol comment).
        /// </summary>
        public byte[] Migrate(byte[] content)
        {
            ArgumentNullException.ThrowIfNull(content);

            string text = ConfigurationText.Decode(content);

            lock (_lock)
            {
                var file = Read(text);

                if (!_cachedMigrated)
                    return content; // current: served as it is on disk

                // the migrated in-memory form - rendered once per distinct text; an own-write
                // intermediate text (a slow reader of a step the file has moved past) is served
                // the same final form
                return _cachedServed ??= XMigrationFileWriter.Render(file.Root.Document!,
                    hadBom: XMigrationFileWriter.HasUtf8Bom(content),
                    newLine: ConfigurationText.DetectNewLine(text));
            }
        }

        /// <summary>
        /// Parses (whitespace-preserving), migrates and wraps the file text — once per distinct
        /// text; a repeated read of the same text is served from the cache. Exceptions propagate
        /// (nothing is cached then).
        /// </summary>
        internal XmlConfigurationFile Read(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            lock (_lock)
            {
                if (_cachedFile is not null && text == _cachedText)
                {
                    // the text that was migrated stays a valid key for a reader that opened the
                    // file before the migration wrote it (the providers and the pipeline read
                    // the same file within milliseconds of each other) - but not for a file
                    // that holds that text AGAIN: then the user reverted it (a restored backup,
                    // an editor's undo), and the migration - and the write - must happen again
                    if (!_cachedWritten || !FileHolds(text))
                        return _cachedFile;
                }

                if (_ownTexts.Contains(text))
                {
                    // one of the texts this run wrote to the file: by construction the same data
                    // as the cached document (or a step on the way to it), so it is served as
                    // is - re-migrating an intermediate step would run the steps (and take the
                    // backups) a second time
                    if (_cachedFile is not null)
                        return _cachedFile;
                }
                else
                {
                    _ownTexts.Clear(); // a foreign text: the own writes are history from here on
                }

                var xml = XmlConfigurationReader.Parse(text);

                bool written = false;
                bool migrated = false;

                if (xml.Root is not null) // a rootless document is reported by the reader below
                {
                    var document = new XMigrationDocument(xml, text, _filePath, Logger);
                    try
                    {
                        Engine.Migrate(document);
                    }
                    finally
                    {
                        // whatever happened, a step that made it to the file is an own write
                        _ownTexts.UnionWith(document.WrittenTexts);

                        written = document.WrittenTexts.Count > 0;
                        migrated = document.Migrated;

                        document.Release();
                    }
                }

                var file = XmlConfigurationReader.Read(xml);

                (_cachedText, _cachedFile, _cachedWritten, _cachedMigrated, _cachedServed) = (text, file, written, migrated, null);

                return file;
            }
        }

        // whether the file on disk currently holds exactly this text (false when it cannot be read)
        private bool FileHolds(string text)
        {
            try
            {
                return _filePath() is string path && File.Exists(path) && ConfigurationText.ReadFile(path) == text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
