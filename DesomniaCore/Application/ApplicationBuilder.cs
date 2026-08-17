using Autofac;
using Autofac.Extensions.DependencyInjection;
using MadWizard.Desomnia.Application;
using MadWizard.Desomnia.Application.Module;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia
{
    /*
     * The builder is long-lived: modules are registered exactly once and survive application
     * restarts. Build() raises the persistent host — a Microsoft.Extensions host whose intrinsic
     * Autofac container is the machine-lifetime scope: it owns the process lifetime, the OS-facing
     * singletons (LoadOnce) and the configuration authorities (pipeline + monitor) — and wraps it
     * in the DesomniaHost, whose loop builds, runs and rebuilds the inner application hosts
     * (BuildApplication), each bridged to that same persistent scope.
     */
    public class ApplicationBuilder
    {
        const string CONFIG_FILE_NAME = "monitor.xml";
        const string NLOG_CONFIG_FILE_NAME = "NLog.config";

        protected readonly ExtendedXmlConfigurationSource _source;

        readonly List<Module> _modules = [];

        // the persistent host and its Autofac container (the machine-lifetime scope). Built once by
        // Build(), disposed only when the whole application stops — NOT on a configuration rebuild,
        // so the services and the OS state they hold survive reconfiguration
        private ILifetimeScope? _root;

        #region Defaults
        protected virtual string DefaultLogLevelFormat => "${pad:padding=5:inner=${level:uppercase=true}}";
        protected virtual string DefaultLogFileLayout => "${longdate} " + DefaultLogLevelFormat + " ${logger:shortName=true} :: ${message} ${exception}";
        protected virtual string DefaultLogConsoleLayout => DefaultLogLevelFormat + " :: ${message} ${exception}";

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

        /**
         * Ideally the ContextRootPath should be left empty,
         * because the runtime will install file system watches
         * for every file below that path. On Linux this can
         * extend to the whole file system, if run as a systemd unit.
         */
        protected virtual HostApplicationBuilderSettings DefaultSettings => new()
        {
            DisableDefaults = true // don't set ContextRootPath to working directory
        };

        protected virtual HostApplicationBuilderSettings DefaultApplicationSettings => new()
        {
            DisableDefaults = true 
        };
        #endregion

        #region Config path lookup
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

        internal ApplicationBuilder(string? configPath = null)
        {
            configPath = Path.GetFullPath(configPath ?? LookupConfigPath());

            _source = new ExtendedXmlConfigurationSource(configPath, optional: false);
        }

        protected ApplicationBuilder(string[] args) : this()
        {
            var result = new ApplicationCommandLine().Parse(args);

            if (_source is FileConfigurationSource file)
            {
                file.ReloadOnChange = result.GetValue(ApplicationCommandLine.AutoReloadOption);
            }

            result.Invoke();
        }

        #region Platform registrations
        public void RegisterModule(Module module)
        {
            _modules.Add(module);
        }

        public void RegisterPluginModules()
        {
            foreach (var path in DefaultPluginsPaths)
            {
                this.RegisterPluginModules(path);
            }
        }
        #endregion

        #region Build Application Host
        /// <summary>
        /// Builds the persistent host — the process-lifetime Microsoft.Extensions host whose
        /// intrinsic Autofac container is the machine-lifetime scope: the real
        /// <see cref="IHostLifetime"/> (the Windows service in service mode, the console lifetime
        /// otherwise), every module's <see cref="Module.LoadOnce(ContainerBuilder, Microsoft.Extensions.Configuration.IConfiguration)"/>
        /// singletons and the configuration authorities — and returns it wrapped in the
        /// <see cref="ApplicationHost"/>, whose loop builds, runs and rebuilds the inner application
        /// hosts. Its configuration is the root configuration (see <see cref="LoadConfiguration"/>),
        /// which every module sees in <see cref="Module.BuildOnce"/> before the container is built.
        /// Only a genuine process stop or a fatal configuration brings it down; a fatal
        /// escapes <see cref="ApplicationHost.Run"/> to the entry point with a non-zero exit code.
        /// </summary>
        public ApplicationHost Build()
        {
            var builder = new HostApplicationBuilder(DefaultSettings);

            LoadConfiguration(builder);

            ConfigureLogging(builder.Logging);
            ConfigureServices(builder.Services);

            builder.ConfigureContainer(new AutofacServiceProviderFactory(), ConfigureContainer);

            foreach (var module in _modules)
            {
                module.BuildOnce(builder);
            }

            var host = builder.Build();

            _root = host.Services.GetAutofacRoot();

            return new ApplicationHost(host, this);
        }

        /// <summary>
        /// Makes the root host's <see cref="HostApplicationBuilder.Configuration"/> the authority
        /// over the root configuration: the physical source's nested root source (the
        /// <c>&lt;?global?&gt;</c> directives; see <see cref="IRootConfigurationSource"/>) is added
        /// like any other source, so the modules read it through the builder in
        /// <see cref="Module.BuildOnce"/> and the persistent services bind it through the standard
        /// options interfaces — an <c>IOptionsMonitor&lt;T&gt;</c> follows the file when auto-reload
        /// is on. A source without root support leaves the configuration empty.
        /// </summary>
        private void LoadConfiguration(HostApplicationBuilder builder)
        {
            if (_source is ExtendedXmlConfigurationSource xml)
            {
                // the collection-element knowledge must exist before the pipeline reads the file
                // (the merger and the flattener need it); contributed by every configurable module
                foreach (var module in _modules.OfType<ConfigurableModule>())
                {
                    module.ConfigureConfigurationSource(xml);
                }
            }

            if (_source is IRootConfigurationSource root)
            {
                builder.Configuration.Sources.Add(root.RootSource);
            }
        }

        protected virtual void ConfigureLogging(ILoggingBuilder builder)
        {
            foreach (var module in _modules)
            {
                LogManager.Setup().SetupExtensions(module.ConfigureLogging);
            }

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
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            builder.AddNLog();
        }

        protected virtual void ConfigureServices(IServiceCollection services) { }

        /// <summary>
        /// Fills the persistent container. <paramref name="configuration"/> is the root host's
        /// configuration (see <see cref="LoadConfiguration"/>), handed to the modules'
        /// <c>LoadOnce</c> as its convenience argument; a value bound from it here is the
        /// boot-time value for the life of the process (only the options interfaces follow a reload).
        /// </summary>
        protected virtual void ConfigureContainer(ContainerBuilder container)
        {
            container.RegisterModule<LoggingModule>();

            container.RegisterModule<FrameworkModule>();

            // the stage between the physical file and the monitor: mode detection, parsing,
            // condition binding, change watching - and the fatal-exit policy for changes the
            // running process cannot apply
            container.RegisterType<ConfigurationPipeline>()
                .WithParameter(TypedParameter.From(_source))
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();

            foreach (var module in _modules)
            {
                module.LoadOnce(container);
            }
        }
        #endregion

        #region Build Application
        /// <summary>
        /// Builds a fresh inner application host for one effective configuration, bridged
        /// to the persistent scope. Disposed (and rebuilt) by the loop on every reconfiguration.
        /// </summary>
        public IHost BuildApplication()
        {
            var builder = new HostApplicationBuilder(DefaultApplicationSettings);

            LoadApplicationConfiguration(builder);

            ConfigureApplicationServices(builder.Services);

            builder.ConfigureContainer(new AutofacServiceProviderFactory(), ConfigureApplicationContainer);

            foreach (var module in _modules)
            {
                module.Build(builder);
            }

            return builder.Build();
        }

        private void LoadApplicationConfiguration(HostApplicationBuilder builder)
        {
            if (_root is not ILifetimeScope scope)
                throw new Exception("Configuration must be loaded after root scope.");

            // start the configuration pipeline now (read the file, feed the monitor, watch
            // for changes), so its change sources run before the loop builds the first inner
            // host. A configuration problem here is fatal - there is nothing to fall back to.
            var pipeline = scope.Resolve<ConfigurationPipeline>();
            var monitor = scope.Resolve<EnvironmentMonitor>();

            monitor.ResetReloadToken();

            builder.Configuration.Sources.Add(pipeline.EffectiveSource);
        }

        private void ConfigureApplicationServices(IServiceCollection services)
        {
            services.RemoveAll<IHostLifetime>(); // start with a blank slate

            // the inner host must NOT own the process lifetime — that belongs to the persistent
            // host alone. A rebuild stops the inner host through the loop's linked token, never
            // through a console/SCM signal, so it gets the no-op lifetime.
            services.AddSingleton<IHostLifetime, NullLifetime>();
        }

        private void ConfigureApplicationContainer(ContainerBuilder container)
        {
            if (_root is not ILifetimeScope scope)
                throw new Exception("Application must be created after root scope.");

            container.RegisterModule<LoggingModule>();

            if (!RuntimeFeature.IsDynamicCodeSupported)
            {
                container.RegisterModule<AOTModule>();
            }

            container.RegisterModule(new FrameworkBridgeModule(scope));

            // takes over the collection relationship (IEnumerable<T>, T[], ...) so .WithPriority() on a
            // registration is all it takes to order it. Must stay a plain RegisterSource call here: the
            // container's default adapters are added before the configuration callbacks run, and the
            // later-added source is the one consulted first.
            container.RegisterSource(new PriorityEnumerationSource());

            foreach (var module in _modules)
            {
                container.RegisterModule(module);
            }
        }
        #endregion
    }
}
