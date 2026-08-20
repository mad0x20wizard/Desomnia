using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Xml.Linq;

namespace MadWizard.Desomnia.Configuration.Xml
{
    /// <summary>
    /// The XML adapter of the migration engine (see <see cref="IMigrationDocument"/>): one parsed
    /// configuration file — the <see cref="XDocument"/> plus the raw text it was parsed from —
    /// with the version read as the file declares it (the root element's attribute and/or the
    /// <c>&lt;?config?&gt;</c> header, see <see cref="XConfigVersion"/>) and the migration
    /// settings read off the header (see <see cref="XConfigDirective"/>), the modules' steps run
    /// through <see cref="IXConfigurationMigration"/> under change tracking, the new version
    /// stamped back — the root element's attribute unconditionally (it is the authoritative
    /// declaration), the header's version only where the user chose to write one there, so the
    /// file keeps its style — and the file written back through <see cref="XMigrationFileWriter"/>.
    /// </summary>
    internal sealed class XMigrationDocument : IMigrationDocument
    {
        readonly XDocument _document;
        readonly string _text;
        readonly Func<string?> _filePath;
        readonly ILogger _logger;

        readonly XMigrationTracker _tracker;
        readonly XMigrationProtocol _protocol;

        readonly List<string> _written = [];

        bool _headerRead;
        XConfigHeader? _header;

        MigrationSettings? _settings;

        /// <param name="text">The raw text the document was parsed from — the file's newline style is taken from it.</param>
        public XMigrationDocument(XDocument document, string text, Func<string?> filePath, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(filePath);
            ArgumentNullException.ThrowIfNull(logger);

            _document = document;
            _text = text;
            _filePath = filePath;
            _logger = logger;

            _tracker = XMigrationTracker.For(document);
            _protocol = new XMigrationProtocol();
        }

        public XDocument Document => _document;

        /// <summary>The texts written to the file by this document's migration, one per step (see the own-write handling of the pipeline).</summary>
        public IReadOnlyList<string> WrittenTexts => _written;

        /// <summary>Whether any migration step stamped this document, i.e. its in-memory shape
        /// differs from the text it was parsed from (see the serving of the migrated form).</summary>
        public bool Migrated { get; private set; }

        public string? FilePath => _filePath();

        private XElement Root => _document.Root ?? throw new ConfigurationValueException("The configuration has no root element.");

        #region Header

        /// <summary>The file's <c>&lt;?config?&gt;</c> header, or null when it has none (read once; the stamp keeps it current).</summary>
        private XConfigHeader? Header
        {
            get
            {
                if (!_headerRead)
                {
                    _header = XConfigDirective.Read(_document);
                    _headerRead = true;
                }

                return _header;
            }
        }

        public uint Version
        {
            get => XConfigVersion.Read(_document, Header);

            set
            {
                using (_tracker.Enter()) // the stamp itself is not a change to report
                {
                    string stamp = value.ToString(CultureInfo.InvariantCulture);

                    // the root element's attribute is the authoritative declaration: stamped
                    // unconditionally (added when the file declared its version elsewhere or
                    // not at all), in whatever casing the user wrote it
                    if (Root.AttributeNamed(XConfigVersion.VERSION_ATTRIBUTE) is XAttribute attribute)
                        attribute.Value = stamp;
                    else
                        Root.Add(new XAttribute(XConfigVersion.VERSION_ATTRIBUTE, stamp));

                    // a header that declares the version keeps declaring it (the user's style)
                    if (Header is { Version: not null } header)
                    {
                        XConfigDirective.SetVersion(header.Instruction, value);

                        _header = header with { Version = value };
                    }
                }

                Migrated = true;
            }
        }

        #endregion

        /// <summary>The migration settings of the file's <c>&lt;?config?&gt;</c> header (see <see cref="XConfigDirective"/>):
        /// a file that says nothing about migration gets the defaults (transient).</summary>
        public MigrationSettings Settings => _settings ??= Header is XConfigHeader header
            ? MigrationSettings.Create(header.AutoMigrate, header.WriteBackup, _filePath())
            : MigrationSettings.Default;

        #region Steps

        public bool Supports(ConfigurableModule module) => module is IXConfigurationMigration;

        public void Apply(ConfigurableModule module, uint version)
        {
            if (module is not IXConfigurationMigration migration)
                throw new ArgumentException($"{module.GetType().Name} does not migrate XML configurations.", nameof(module));

            // every change the step makes past the helpers is recorded as a warning
            _tracker.Listen();
            try
            {
                migration.Run(_document, version);
            }
            finally
            {
                _tracker.Unlisten();
            }
        }

        public IReadOnlyList<MigrationAnnotation> TakeAnnotations() => _tracker.Take();

        #endregion

        public void Write(string? backupPath, uint sourceVersion, uint targetVersion, IReadOnlyList<MigrationAnnotation> notes)
        {
            string? path = _filePath();

            if (path is null || !File.Exists(path))
                throw new IOException("There is no configuration file to update.");

            // the writer's own edits (the protocol comment) are not changes to report
            using (_tracker.Enter())
                _written.Add(XMigrationFileWriter.Write(_document, path, backupPath, sourceVersion, targetVersion, notes, _protocol, _text, _logger));
        }

        /// <summary>Ends the migration: the document leaves without tracker (no live subscription, no stale notes).</summary>
        public void Release() => _tracker.Release();
    }
}
