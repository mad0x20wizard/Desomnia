using Autofac;
using Autofac.Extensions.DependencyInjection;
using MadWizard.Desomnia;
using MadWizard.Desomnia.Application.Lifetime;
using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Application.Shutdown;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Xml;
using MadWizard.Desomnia.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Application
{
    /*
     * The builder is long-lived: modules are registered exactly once and survive application
     * restarts. Build() raises the persistent host — a Microsoft.Extensions host whose intrinsic
     * Autofac container is the machine-lifetime scope: it owns the process lifetime, the OS-facing
     * singletons (LoadOnce) and the configuration authorities (pipeline + monitor) — and wraps it
     * in the DesomniaHost, whose loop builds, runs and rebuilds the inner application hosts
     * (BuildApplication), each bridged to that same persistent scope.
     */
    public abstract class ApplicationBuilder
    {
        protected abstract IConfigurationSource Source { get; }

        // the central authority over the module system: the registered modules and everything
        // derived from the set - the format algebra and the version check that refuses a
        // mismatched file (the pipeline runs it), with or without the migration layer attached
        internal readonly ModuleRegistry _registry = new();

        // the persistent host and its Autofac container (the machine-lifetime scope). Built once by
        // Build(), disposed only when the whole application stops — NOT on a configuration rebuild,
        // so the services and the OS state they hold survive reconfiguration
        private ILifetimeScope? _root;

        public void RegisterModule(Module module)
        {
            _registry.Register(module);
        }

        #region Build Application Host
        protected virtual HostApplicationBuilderSettings DefaultSettings => new()
        {
            DisableDefaults = true // don't set ContextRootPath to working directory
        };

        /// <summary>
        /// Builds the persistent host — the process-lifetime Microsoft.Extensions host whose
        /// intrinsic Autofac container is the machine-lifetime scope: the real
        /// <see cref="IHostLifetime"/> (the Windows service in service mode, the console lifetime
        /// otherwise), every module's <see cref="Module.LoadOnce(ContainerBuilder, Microsoft.Extensions.Configuration.IConfiguration)"/>
        /// singletons and the configuration authorities — and returns it wrapped in the
        /// <see cref="ApplicationHost"/>, whose loop builds, runs and rebuilds the inner application
        /// hosts. Its configuration is the root configuration (see <see cref="LoadConfiguration"/>),
        /// which every module sees in <see cref="Module.BuildOnce"/> before the container is built.
        /// Logging is configured before the configuration is loaded, because loading the root source
        /// reads (and possibly migrates) the file immediately.
        /// Only a genuine process stop or a fatal configuration brings it down; a fatal
        /// escapes <see cref="ApplicationHost.Run"/> to the entry point with a non-zero exit code.
        /// </summary>
        public ApplicationHost Build()
        {
            var builder = new HostApplicationBuilder(DefaultSettings);

            // logging first: adding the root source below reads the file at once, and an
            // outdated file is migrated right there - which must be able to log
            ConfigureLogging(builder.Logging);

            LoadConfiguration(builder);

            ConfigureServices(builder.Services);

            builder.ConfigureContainer(new AutofacServiceProviderFactory(), ConfigureContainer);

            foreach (var module in _registry.Modules)
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
        /// <c>&lt;?system?&gt;</c> directives; see <see cref="IRootConfigurationSource"/>) is added
        /// like any other source, so the modules read it through the builder in
        /// <see cref="Module.BuildOnce"/> and the persistent services bind it through the standard
        /// options interfaces — an <c>IOptionsMonitor&lt;T&gt;</c> follows the file when auto-reload
        /// is on. A source without root support leaves the configuration empty.
        /// </summary>
        protected virtual void LoadConfiguration(HostApplicationBuilder builder)
        {
            if (Source is IRootConfigurationSource root)
            {
                builder.Configuration.Sources.Add(root.RootSource);
            }

            // fix the format algebra now (all modules are registered) - an unsatisfiable module
            // set fails the boot deterministically, even when the configuration file is missing
            _ = _registry.RequiredVersion;
        }

        protected virtual void ConfigureLogging(ILoggingBuilder builder)
        {
            foreach (var module in _registry.Modules)
            {
                LogManager.Setup().SetupExtensions(module.ConfigureLogging);
            }
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
            // condition binding, change watching, the version check - and the fatal-exit
            // policy for changes the running process cannot apply. It reads the file through
            // the source's provider, like every other consumer - the migration layer (when
            // composed underneath, see AttachMigration) is invisible to it
            container.RegisterType<ConfigurationPipeline>()
                .WithParameter(TypedParameter.From((ExtendedXmlConfigurationSource)Source)) // FIXME
                .WithParameter(TypedParameter.From(_registry))
                .AsImplementedInterfaces().AsSelf()
                .SingleInstance();

            foreach (var module in _registry.Modules)
            {
                module.LoadOnce(container);
            }
        }
        #endregion

        #region Build Application
        protected virtual HostApplicationBuilderSettings DefaultApplicationSettings => new()
        {
            DisableDefaults = true
        };

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

            foreach (var module in _registry.Modules)
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

            foreach (var module in _registry.Modules)
            {
                container.RegisterModule(module);
            }
        }
        #endregion
    }
}
