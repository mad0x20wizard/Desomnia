using Autofac;
using MadWizard.Desomnia.Configuration.Migration;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace MadWizard.Desomnia.Application
{
    /// <summary>
    /// The central authority over the module system: every <see cref="Module"/> of the product
    /// is registered here exactly once (the <see cref="ApplicationBuilder"/> creates and keeps
    /// the registry for the life of the process), and everything the product derives FROM the
    /// registered module set is answered here — today the configuration format algebra over the
    /// modules' version facts (<see cref="ConfigurableModule.MinVersion"/>,
    /// <see cref="ConfigurableModule.MaxVersion"/>) and the version check built on it.
    ///
    /// <para><see cref="SupportedVersion"/> is the newest format version every module accepts,
    /// <see cref="RequiredVersion"/> the oldest version every module still reads without
    /// migration — so a file older than the product's latest format stays perfectly valid as
    /// long as no loaded module demands a newer one. <see cref="Validate"/> is the authority
    /// that REFUSES a mismatched file: a document newer than <see cref="SupportedVersion"/>
    /// (a newer configuration on an older build), or one still older than
    /// <see cref="RequiredVersion"/> after the migration had its chance. It stands apart from
    /// the migration layer on purpose: the check holds with the migration detached
    /// (<c>autoMigrate="never"</c>, a composition without the migrating file provider) —
    /// migration is one way to satisfy it, not its owner.</para>
    ///
    /// <para>The algebra is fixed at the first use: registrations (and a change of the latest
    /// version) are refused from then on. Everything is logged before an exception stops the
    /// application, so the log has the specifics.</para>
    /// </summary>
    internal sealed class ModuleRegistry
    {
        readonly List<Module> _modules = [];

        // guards the registrations and the algebra
        readonly Lock _lock = new();

        ILogger? _logger;

        uint _latest = ConfigurableModule.LATEST_VERSION;

        // the version algebra, computed lazily from the registered modules at the first use;
        // registrations (and a change of the latest version) are refused from then on
        (uint Supported, uint Required, ConfigurableModule? MostDemanding)? _versions;

        /// <summary>
        /// The registry's log — module-set diagnostics (the algebra's warnings), under the
        /// registry's own category; the migration engine logs its runs under its own (see
        /// <c>ConfigurationMigrator.Logger</c>). Created lazily and NLog-backed by default,
        /// so it follows the NLog configuration made before the first read (the application
        /// builder configures logging before it loads the configuration); tests inject a
        /// capturing logger.
        /// </summary>
        internal ILogger Logger
        {
            get => _logger ??= new NLogLoggerFactory().CreateLogger(typeof(ModuleRegistry).FullName!);
            set => _logger = value;
        }

        /// <summary>The newest format version this build knows. Test seam (simulates a future
        /// format); fixed once the algebra has been computed.</summary>
        internal uint LatestVersion
        {
            get => _latest;
            set
            {
                lock (_lock)
                {
                    if (_versions is not null)
                        throw new InvalidOperationException("The latest version must be set before the configuration is read for the first time.");

                    _latest = value;
                }
            }
        }

        /// <summary>The registered modules, in registration order.</summary>
        internal IReadOnlyList<Module> Modules => _modules;

        /// <summary>The registered modules that carry configuration (and thereby version facts), in registration order.</summary>
        internal IEnumerable<ConfigurableModule> ConfigurableModules => _modules.OfType<ConfigurableModule>();

        /// <summary>
        /// Registers a module — exactly once, for the life of the process. A configurable
        /// module thereby takes part in the format algebra (and in the migration steps, in
        /// registration order). Only possible before the first use of the algebra: the module
        /// set is fixed then.
        /// </summary>
        internal void Register(Module module)
        {
            ArgumentNullException.ThrowIfNull(module);

            lock (_lock)
            {
                if (_versions is not null)
                    throw new InvalidOperationException("Modules must be registered before the configuration is read for the first time.");

                _modules.Add(module);
            }
        }

        #region Plugins
        public void RegisterPluginModules(string path)
        {
            foreach (var assembly in EnumPlugins(path))
            {
                RegisterPluginAssembly(assembly);
            }
        }

        private void RegisterPluginAssembly(Assembly assembly)
        {
            var moduleFinder = new ContainerBuilder();

            moduleFinder.RegisterAssemblyTypes(assembly)
                .Where(t => typeof(Module).IsAssignableFrom(t))
                .PropertiesAutowired()
                .As<Module>();

            using (var moduleContainer = moduleFinder.Build())
            {
                foreach (var module in moduleContainer.Resolve<IEnumerable<Module>>())
                {
                    Register(module);
                }
            }
        }

        private static IEnumerable<Assembly> EnumPlugins(string path)
        {
            if (Directory.Exists(path = Path.GetFullPath(path)))
            {
                // extract path
                foreach (var zipFile in Directory.GetFiles(path, "plugin-*.zip"))
                {
                    // example: "plugin-FirewallKnockOperator_v3.0.0-beta8.zip"
                    var name = Path.GetFileNameWithoutExtension(zipFile);
                    name = name.Replace("plugin-", string.Empty);
                    name = name.Split("_")[0];

                    ZipFile.ExtractToDirectory(zipFile, Path.Combine(path, name));
                    File.Delete(zipFile);
                }

                // register path
                foreach (var pluginDir in Directory.GetDirectories(path))
                {
                    var pluginName = new DirectoryInfo(pluginDir).Name;
                    var pluginPath = Path.Combine(pluginDir, $"{pluginName}.dll");

                    if (File.Exists(pluginPath))
                    {
                        var pluginContext = new PluginLoadContext(pluginPath);

                        yield return pluginContext.PluginAssembly;
                    }
                }
            }
        }
        #endregion

        #region Versioning

        /// <summary>The newest format version every registered module accepts (see the algebra).</summary>
        internal uint SupportedVersion => Versions.Supported;

        /// <summary>The oldest format version every registered module reads without migration.</summary>
        internal uint RequiredVersion => Versions.Required;

        /// <summary>
        /// The registered modules whose <see cref="ConfigurableModule.MinVersion"/> lies beyond
        /// the given document version — the participants of a migration starting there, in
        /// registration order. Computing this fixes the algebra.
        /// </summary>
        internal IReadOnlyList<ConfigurableModule> ParticipantsBeyond(uint version)
        {
            _ = Versions; // fixes the algebra: the participant list must not change afterwards

            lock (_lock) return _modules.OfType<ConfigurableModule>().Where(module => module.MinVersion > version).ToList();
        }

        /// <summary>
        /// Checks a document's (post-migration) version against the algebra and throws a
        /// <see cref="ConfigurationMigrationException"/> for a document this build cannot use:
        /// one NEWER than <see cref="SupportedVersion"/> (a newer configuration on an older
        /// build), or one still OLDER than <see cref="RequiredVersion"/> — which, after the
        /// migration had its chance, means migration was disabled or detached. Deliberately
        /// independent of the migration layer (see the class summary).
        /// </summary>
        internal void Validate(uint version)
        {
            var (supported, required, mostDemanding) = Versions;

            if (version > supported)
                throw new ConfigurationMigrationException($"The configuration file uses format version {version}, " +
                    $"but this build supports at most version {supported}. The file seems to belong to a newer version of the software.");

            if (version < required)
            {
                string demanding = mostDemanding is not null
                    ? $"{mostDemanding.GetType().Name} requires at least version {required}"
                    : $"this build requires at least version {required}";

                throw new ConfigurationMigrationException($"The configuration file uses format version {version}, " +
                    $"but {demanding}. Update the configuration file manually, or declare " +
                    $"{MigrationSettings.AUTO_MIGRATE_KEY}=\"transient\" or \"persistent\" in the <?config?> header " +
                    "to have it migrated automatically.");
            }
        }

        private (uint Supported, uint Required, ConfigurableModule? MostDemanding) Versions
        {
            get
            {
                lock (_lock) return _versions ??= ComputeVersions();
            }
        }

        // under _lock
        private (uint Supported, uint Required, ConfigurableModule? MostDemanding) ComputeVersions()
        {
            uint latest = _latest;

            uint supported = latest;
            uint required = 1;

            ConfigurableModule? mostDemanding = null;
            ConfigurableModule? mostRestrictive = null;

            foreach (var module in _modules.OfType<ConfigurableModule>())
            {
                uint max = module.MaxVersion;
                uint min = module.MinVersion;

                if (max < latest)
                    Logger.LogWarning($"{module.GetType().Name} limits the configuration format to version {max} (current: {latest}).");

                if (max < supported)
                    (supported, mostRestrictive) = (max, module);

                if (min > required)
                    (required, mostDemanding) = (min, module);
            }

            if (required > supported)
            {
                // name the culprits - either side may be the product itself (no module raised
                // the requirement above the baseline, or none limits the format below latest)
                string demanding = mostDemanding is not null ? $"{mostDemanding.GetType().Name} requires version {required}" : $"the format requires at least version {required}";
                string restrictive = mostRestrictive is not null ? $"{mostRestrictive.GetType().Name} supports at most version {supported}" : $"this build supports at most version {supported}";

                throw new ConfigurationMigrationException($"The configuration format cannot be satisfied: {demanding}, but {restrictive}.");
            }

            return (supported, required, mostDemanding);
        }

        #endregion
    }

    file class PluginLoadContext(string path) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver = new(path);

        protected override Assembly? Load(AssemblyName assemblyName) => resolver.ResolveAssemblyToPath(assemblyName) is string assemblyPath ? LoadFromAssemblyPath(assemblyPath) : null;

        protected override nint LoadUnmanagedDll(string unmanagedDllName) => resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is string libraryPath ? LoadUnmanagedDllFromPath(libraryPath) : 0;

        public Assembly PluginAssembly => LoadFromAssemblyName(AssemblyName.GetAssemblyName(path));
    }
}
