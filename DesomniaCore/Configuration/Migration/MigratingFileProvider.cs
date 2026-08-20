using MadWizard.Desomnia.Application;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using NLog;

namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// The format seam of the <see cref="MigratingFileProvider"/>: one file format's migration
    /// front (XML: <c>Configuration.Xml.XConfigurationMigrator</c>). Takes the file's raw
    /// content, returns the content to serve — the input itself when the file is current, the
    /// migrated form otherwise. Expected to cache per distinct content (the provider calls it
    /// on every read) and to throw when the content cannot be served (a malformed file, a
    /// failing migration step).
    /// </summary>
    internal interface IConfigurationFileMigrator
    {
        byte[] Migrate(byte[] content);
    }

    /// <summary>
    /// The migration layer as a file-provider decorator: sits between the physical file and the
    /// configuration source (<see cref="Microsoft.Extensions.Configuration.FileConfigurationSource.FileProvider"/>),
    /// so every consumer of the source — the content provider, the nested root provider, the
    /// configuration pipeline, anyone watching the same provider — transparently reads the
    /// migrated form of the configuration file while none of them knows migration exists.
    /// Remove this decorator and the source serves the file as-is; the version check
    /// (<see cref="ModuleRegistry.Validate"/>) still refuses a mismatched file.
    ///
    /// <para>Only the configured file passes through the migrator; any other path is served
    /// untouched. The physical stream is read completely and CLOSED before the migrator runs,
    /// so a persistent migration can atomically replace the file no reader holds open.</para>
    ///
    /// <para>A failing read or migration always THROWS — a configuration that cannot be served
    /// stops the application, by design (the service manager's restart is the recovery path).
    /// At boot the exception surfaces through the normal startup error handling; on a RELOAD it
    /// escapes the stock provider machinery's change callback (which reads outside its own
    /// error handling) and terminates the process as an unhandled exception — so the error is
    /// logged AND FLUSHED here first, because no orderly shutdown will do it later.</para>
    /// </summary>
    internal sealed class MigratingFileProvider : IFileProvider
    {
        static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        readonly IFileProvider _inner;
        readonly IConfigurationFileMigrator _migrator;
        readonly string _path;

        bool _served; // whether the file was served successfully at least once (boot vs. reload)

        /// <param name="inner">The physical provider to decorate.</param>
        /// <param name="migrator">The format-matching migration front.</param>
        /// <param name="path">The configuration file's path relative to the provider (the source's <c>Path</c>).</param>
        public MigratingFileProvider(IFileProvider inner, IConfigurationFileMigrator migrator, string path)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(migrator);
            ArgumentException.ThrowIfNullOrEmpty(path);

            _inner = inner;
            _migrator = migrator;
            _path = path;
        }

        /// <summary>The decorated (physical) provider — for whoever needs the file's real location.</summary>
        internal IFileProvider InnerProvider => _inner;

        public IFileInfo GetFileInfo(string subpath)
        {
            var file = _inner.GetFileInfo(subpath);

            return Matches(subpath) ? new MigratedFileInfo(file, this) : file;
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => _inner.GetDirectoryContents(subpath);

        public IChangeToken Watch(string filter) => _inner.Watch(filter);

        private bool Matches(string subpath)
            => string.Equals(subpath.TrimStart('/', '\\'), _path.TrimStart('/', '\\'), StringComparison.OrdinalIgnoreCase);

        private byte[] ReadMigrated(IFileInfo file)
        {
            byte[] content;

            // read completely and dispose BEFORE migrating: a persistent migration replaces the
            // file, which an open handle would refuse (Windows)
            using (var stream = file.CreateReadStream())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);

                content = buffer.ToArray();
            }

            try
            {
                byte[] served = _migrator.Migrate(content);

                _served = true;

                return served;
            }
            catch (Exception ex) when (_served)
            {
                // the reload path: this exception escapes the stock provider machinery's change
                // callback and terminates the process (see the class summary) - no orderly
                // shutdown will flush the log, so it happens here, before the throw
                Logger.Error(ex, $"Failed to read '{file.Name}'; the configuration cannot be served, the application stops.");

                LogManager.Flush(TimeSpan.FromSeconds(2));

                throw;
            }
        }

        /// <summary>The configuration file as its consumers see it: existence, name and timestamp
        /// of the physical file, content (and length) of the migrated form.</summary>
        private sealed class MigratedFileInfo(IFileInfo inner, MigratingFileProvider provider) : IFileInfo
        {
            public bool Exists => inner.Exists;

            public bool IsDirectory => inner.IsDirectory;

            public string Name => inner.Name;

            /// <summary>Deliberately null: a non-null physical path invites consumers (notably
            /// <c>FileConfigurationProvider.OpenRead</c>) to bypass <see cref="CreateReadStream"/>
            /// and read the raw file directly — the migrated form would never be served. The
            /// file's real location is available through the provider's inner provider.</summary>
            public string? PhysicalPath => null;

            public DateTimeOffset LastModified => inner.LastModified;

            public long Length => provider.ReadMigrated(inner).LongLength;

            public Stream CreateReadStream() => new MemoryStream(provider.ReadMigrated(inner), writable: false);
        }
    }
}
