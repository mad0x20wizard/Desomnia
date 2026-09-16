using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace MadWizard.Desomnia.Configuration.Migration
{
    /// <summary>
    /// The file-format-agnostic configuration migration engine: for a document older than the
    /// <see cref="ModuleRegistry.RequiredVersion"/> — i.e. only when a loaded module
    /// actually DEMANDS a newer format — it walks the document up one version at a time through
    /// the modules that can migrate it, applying the <c>never|transient|persistent</c> policy:
    /// what a warning means, when the file is written. The document itself is behind an
    /// <see cref="IMigrationDocument"/> adapter contributed by the file format (XML:
    /// <c>Configuration.Xml.XMigrationDocument</c>); the engine never touches a representation.
    ///
    /// <para>The engine migrates, nothing more: whether the resulting version is acceptable is
    /// <see cref="ModuleRegistry.Validate"/>'s business, checked AFTER the migration and
    /// independent of it — so a document the migration may not touch (<c>autoMigrate="never"</c>)
    /// passes through here quietly and is refused there. A migration that was demanded and
    /// allowed but cannot run (a participant without format support, a failing step, a step
    /// that needs manual attention) is still an exception here — that is a broken migration,
    /// not a version mismatch. Everything is logged before an exception stops the application,
    /// so the log has the specifics.</para>
    /// </summary>
    internal sealed class ConfigurationMigrator
    {
        ILogger? _logger;

        public ConfigurationMigrator(VersionedModuleRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            Registry = registry;
        }

        /// <summary>The module registry: the format algebra whose <see cref="ModuleRegistry.RequiredVersion"/> decides what to migrate.</summary>
        internal VersionedModuleRegistry Registry { get; }

        /// <summary>
        /// The migration log — the engine's OWN category, so migration lines are attributable
        /// (and filterable) as such; the registry's module-set diagnostics log under its own
        /// (see <see cref="ModuleRegistry.Logger"/>). Created lazily and NLog-backed by
        /// default, so it follows the NLog configuration made before the first read; tests
        /// inject a capturing logger.
        /// </summary>
        internal ILogger Logger
        {
            get => _logger ??= new NLogLoggerFactory().CreateLogger(typeof(ConfigurationMigrator).FullName!);
            set => _logger = value;
        }

        #region Migration

        /// <summary>
        /// Migrates the document in place when a loaded module demands a newer format version
        /// (see the class summary for the policy). Returns quietly for a document that is
        /// current enough — or one that migration may not touch (the version check refuses it
        /// later). Throws a <see cref="ConfigurationMigrationException"/> when a demanded
        /// migration is unsupported or a step needs manual attention — and a
        /// <see cref="ConfigurationValueException"/> for a bad value in the document.
        /// </summary>
        internal void Migrate(IMigrationDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            var settings = document.Settings;

            uint version = document.Version;

            if (settings.BackupPattern is not null && !settings.Option.HasFlag(MigrationOption.Persistent))
                Logger.LogDebug($"{MigrationSettings.WRITE_BACKUP_KEY} has no effect without {MigrationSettings.AUTO_MIGRATE_KEY}=\"persistent\".");

            uint required = Registry.RequiredVersion;

            if (version >= required)
                return; // current enough for every loaded module (or newer than the build - not this layer's business)

            if (settings.Option == MigrationOption.None)
            {
                // the version check is the one that refuses the file - with the specifics
                Logger.LogDebug($"The configuration file uses format version {version} and needs version {required}, " +
                    $"but automatic migration is not allowed ({MigrationSettings.AUTO_MIGRATE_KEY}=\"never\").");
                return;
            }

            // every module that takes part in any of the steps ahead (its MinVersion lies beyond
            // the file's version) must be able to migrate this document's format - checked
            // BEFORE the first step runs, so a hard error never follows a half-done (or
            // half-written) migration
            var needMigration = Registry.OfType<ConfigurableModule>().Where(module => module.MinVersion > version).ToList();

            if (needMigration.FirstOrDefault(module => !document.Supports(module)) is ConfigurableModule unsupported)
                throw new ConfigurationMigrationException($"<{unsupported.GetType().FullName}> requires configuration format version {unsupported.MinVersion}, " +
                    $"but cannot migrate this configuration automatically. Update the configuration file from version {version} to version {required} manually.");

            Run(document, needMigration, version, required, settings);
        }

        private void Run(IMigrationDocument document, IReadOnlyList<ConfigurableModule> modules, uint version, uint required, MigrationSettings settings)
        {
            bool writing = settings.Option.HasFlag(MigrationOption.Persistent);
            bool transient = settings.Option.HasFlag(MigrationOption.Transient);

            for (uint target = version + 1; target <= required; target++)
            {
                uint source = target - 1;

                Logger.LogWarning($"Migrating the configuration from version {source} -> {target}...");

                foreach (var module in modules.Where(module => module.MinVersion >= target))
                {
                    try
                    {
                        document.Apply(module, target);
                    }
                    catch (ConfigurationValueException)
                    {
                        throw; // a hard configuration error, reported as such
                    }
                    catch (ConfigurationMigrationException)
                    {
                        throw; // already a migration problem in its own words
                    }
                    catch (Exception ex)
                    {
                        throw new ConfigurationMigrationException($"{module.GetType().Name} failed to migrate the configuration " +
                            $"from version {source} -> {target}: {ex.Message}", ex);
                    }
                }

                // the stamp belongs to the step
                document.Version = target;

                var notes = document.TakeAnnotations();

                foreach (var note in notes.Where(note => note.Level != LogLevel.None))
                    Logger.Log(note.Level, $"{note.Path} -> {note.Message}");

                int problems = notes.Count(note => note.Level > LogLevel.Warning && note.Level != LogLevel.None);

                if (problems > 0)
                    throw new ConfigurationMigrationException($"The configuration cannot be migrated automatically from version {source} " +
                        $"to version {target}; {problems} problem(s) need manual attention (see the log).");

                bool warned = notes.Any(note => note.Level == LogLevel.Warning);

                if (!writing)
                    continue;

                if (warned)
                {
                    // a warning means "look at this": the file must not silently take a shape
                    // the user has not seen; from here on the migration lives in memory only
                    writing = false;

                    if (!transient)
                        throw new ConfigurationMigrationException($"The migration from version {source} to version {target} produced warnings, " +
                            $"so the configuration file was not updated ({MigrationSettings.AUTO_MIGRATE_KEY}=\"persistent\"). " +
                            "Review the warnings and update the file manually, or allow \"transient|persistent\" to continue in memory.");

                    Logger.LogWarning($"The migration produced warnings; the configuration file stays at version {source} from here on, " +
                        "the migration continues in memory only.");

                    continue;
                }

                try
                {
                    string? backupPath = document.FilePath is string path ? settings.ResolveBackupPath(source, path) : null;

                    document.Write(backupPath, source, target, notes);

                    Logger.LogInformation($"Configuration file updated to version {target}{(backupPath is not null ? $", backup: {backupPath}" : "")}.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    writing = false;

                    if (!transient)
                        throw new ConfigurationMigrationException("Failed to write the migrated configuration file " +
                            $"({MigrationSettings.AUTO_MIGRATE_KEY}=\"persistent\"): {ex.Message}", ex);

                    Logger.LogWarning(ex, "Failed to write the migrated configuration file; continuing in memory only.");
                }
            }
        }

        #endregion
    }
}
