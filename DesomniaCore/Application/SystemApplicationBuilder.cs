using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Migration;
using MadWizard.Desomnia.Configuration.Model;
using MadWizard.Desomnia.Configuration.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using System.CommandLine;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace MadWizard.Desomnia.Application
{
    public class SystemApplicationBuilder : ApplicationBuilder
    {
        protected override FileConfigurationSource Source { get; }

        protected virtual RootCommand CommandLine => new SystemDefaultCommandLine();

        #region Default paths and lookup
        const string CONFIG_FILE_NAME = "monitor.xml";
        const string NLOG_CONFIG_FILE_NAME = "NLog.config";

        protected virtual string[] DefaultConfigPaths
        {
            get
            {
                List<string> paths = [];

                paths.Add(Directory.GetCurrentDirectory());

                paths.Add(Path.Combine(Directory.GetCurrentDirectory(), "config"));

                if (Environment.GetEnvironmentVariable("DESOMNIA_CONFIG_DIR") is string config)
                    paths.Add(config);

                return [.. paths];
            }
        }

        protected virtual string[] DefaultPluginsPaths
        {
            get
            {
                List<string> paths = [];

                if (Environment.GetEnvironmentVariable("DESOMNIA_PLUGINS_DIR") is string plugins)
                    paths.Add(plugins);
                if (Environment.GetEnvironmentVariable("DESOMNIA_CORE_PLUGINS_DIR") is string core)
                    paths.Add(core);
                if (Environment.GetEnvironmentVariable("DESOMNIA_USER_PLUGINS_DIR") is string user)
                    paths.Add(user);

                return paths.Count > 0 ? [.. paths] : ["plugins"];
            }
        }

        protected virtual string DefaultLogPath
        {
            get
            {
                if (Environment.GetEnvironmentVariable("DESOMNIA_LOG_DIR") is string logs)
                    return logs;

                return "${currentdir:dir=logs}";
            }
        }

        protected virtual string DefaultLogLevelFormat => "${pad:padding=5:inner=${level:uppercase=true}}";
        protected virtual string DefaultLogFileLayout => "${longdate} " + DefaultLogLevelFormat + " ${logger:shortName=true} :: ${message} ${exception}";
        protected virtual string DefaultLogConsoleLayout => DefaultLogLevelFormat + " :: ${message} ${exception}";

        private static string? LookupPath(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (Path.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            return null;
        }

        protected virtual string LookupConfigPath()
        {
            return LookupPath(DefaultConfigPaths.Select(p => Path.Combine(p, CONFIG_FILE_NAME))) ?? CONFIG_FILE_NAME;
        }
        #endregion

        internal SystemApplicationBuilder(string? configPath = null)
        {
            configPath = Path.GetFullPath(configPath ?? LookupConfigPath());

            Source = new ExtendedXmlConfigurationSource(configPath, optional: false);
        }

        protected SystemApplicationBuilder(string[] args) : this()
        {
            var result = CommandLine.Parse(args);

            if (Source is FileConfigurationSource file)
            {
                file.ReloadOnChange = result.GetValue(SystemDefaultCommandLine.AutoReloadOption);
            }

            result.Invoke();
        }

        #region Plugin loading
        public void RegisterPluginModules()
        {
            foreach (var path in DefaultPluginsPaths)
            {
                foreach (var assembly in EnumPlugins(path))
                {
                    _registry.RegisterPluginAssembly(assembly);
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

        #region Logging
        protected override void ConfigureLogging(ILoggingBuilder builder)
        {
            base.ConfigureLogging(builder);

            if (LookupPath(DefaultConfigPaths.Select(p => Path.Combine(p, NLOG_CONFIG_FILE_NAME))) is string configNLogPath)
            {
                LogManager.Configuration = new XmlLoggingConfiguration(configNLogPath);
            }

            if (LogManager.Configuration is LoggingConfiguration config)
            {
                if (!config.Variables.ContainsKey("logDir"))
                {
                    config.Variables["logDir"] = DefaultLogPath;
                }

                if (!config.Variables.ContainsKey("sharedLayout"))
                {
                    config.Variables["sharedLayout"] = DefaultLogFileLayout;
                }
            }
            else // Fallback if no config file has been found
            {
                config = new LoggingConfiguration();
            }

            LogManager.ConfigurationChanged += (sender, args) =>
            {
                if (args.ActivatedConfiguration is LoggingConfiguration configNew && !configNew.HasConsoleTarget())
                {
                    var target = new ConsoleTarget("console")
                    {
                        Layout = DefaultLogConsoleLayout
                    };

                    configNew.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, target, "MadWizard.Desomnia.*");

                    LogManager.Configuration = configNew;
                }
            };

            LogManager.Configuration = config;

            // the process's one logging stack: NLog's LogManager is global, so the provider that
            // fronts it belongs to the host that lives as long as the process. The inner hosts
            // share this factory instead of each bringing their own (see ConfigureApplication).
            // FIXME: Can this be moved into base class?
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            builder.AddNLog();
        }
        #endregion

        #region Configuration / Migration
        protected override void LoadConfiguration(HostApplicationBuilder builder)
        {
            if (Source is ExtendedXmlConfigurationSource xml)
            {
                // the collection-element knowledge must exist before the pipeline reads the file
                // (the merger and the flattener need it); derived from the modules' configuration
                // types — an XML-only concern, so the derivation happens HERE, in the format
                // dispatch, and never inside the module API
                xml.Collections = CollectionElements.Derive(_registry.ConfigTypes);

                AttachMigration(xml);
            }

            base.LoadConfiguration(builder);
        }

        /// <summary>
        /// Composes the migration layer UNDERNEATH the source, as one isolated operation: the
        /// source's file provider is decorated (see <see cref="MigratingFileProvider"/>), so every
        /// consumer of the file transparently reads the migrated form while the source itself
        /// stays pristine. Skip this call and the source serves the file as-is — the version
        /// check (<see cref="ModuleRegistry.Validate"/>, run by the pipeline) still
        /// terminates the application on a mismatch.
        /// </summary>
        private void AttachMigration(ExtendedXmlConfigurationSource xml)
        {
            if (xml.FileProvider is not { } provider || xml.Path is not string path)
                throw new InvalidOperationException("The configuration source has no file provider to decorate " +
                    "(the migration layer needs the resolved provider of an absolute path).");

            var migrator = new XConfigurationMigrator(_registry, () => xml.FullPath);

            xml.FileProvider = new MigratingFileProvider(provider, migrator, path);
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
