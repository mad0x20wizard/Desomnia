using Autofac;
using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using Microsoft.Extensions.Hosting;
using System.Xml.Linq;

namespace MadWizard.Desomnia
{
    public abstract class ConfigurableModule : Module
    {
        private IConfiguration? _rootConfig;

        protected internal virtual uint MinVersion => 1;
        protected internal virtual uint MaxVersion => 1;

        protected internal virtual XDocument MigrateConfiguration(XDocument configuration, uint version) => configuration;

        protected internal virtual void ConfigureConfigurationSource(ExtendedXmlConfigurationSource source) { }

        #region Root configuration
        protected internal override void BuildOnce(HostApplicationBuilder builder)
        {
            base.BuildOnce(builder);

            _rootConfig = builder.Configuration;
        }

        protected internal override void LoadOnce(ContainerBuilder builder) => LoadOnce(builder, _rootConfig!);

        /// <summary>
        /// The configuration-aware convenience overload of <see cref="LoadOnce(ContainerBuilder)"/>:
        /// it receives the root host's configuration — the same <see cref="IConfiguration"/> as
        /// <see cref="BuildOnce"/>'s <c>builder.Configuration</c>, i.e. the <c>&lt;?global key="value"?&gt;</c>
        /// directives of the configuration file — so a module can bind its own process-bound
        /// options (each binds the sections it needs, ignoring the rest) and register accordingly.
        /// The configuration may be empty (root configuration is optional); bind against defaults.
        /// A value bound here is the boot-time value for the life of the process — for one that
        /// follows the file, wire an <c>IOptionsMonitor&lt;T&gt;</c> in <see cref="BuildOnce"/>
        /// instead. The default implementation forwards to the argument-less overload.
        /// </summary>
        protected virtual void LoadOnce(ContainerBuilder builder, IConfiguration config) { }
        #endregion

        static protected T Bind<T>(IConfiguration configuration)
        {
            // the strict binder returns null only for a configuration with no keys at all
            // (invalid values throw instead) - a legitimately empty root configuration
            // then binds to the type's defaults
            return StrictConfigurationBinder.Get<T>(configuration, opt => opt.BindNonPublicProperties = true) ?? Activator.CreateInstance<T>();
        }
    }

    public abstract class ConfigurableModule<T> : ConfigurableModule
    {
        // bound anew for every application instance — derived modules receive it as the
        // config parameter of their Load overload instead of touching mutable state
        private T Config { get; set; } = default!;

        protected internal override void ConfigureConfigurationSource(ExtendedXmlConfigurationSource source)
        {
            base.ConfigureConfigurationSource(source);

            // Derive the names of nameless collection elements from the config type,
            // so the provider can synthesize deterministic name attributes for them.
            source.AddCollectionElementsOf(typeof(T));
        }

        #region Application configuration
        protected internal override void Build(HostApplicationBuilder builder)
        {
            base.Build(builder);

            // Use the strict vendored binder: unknown keys stay tolerated (open format),
            // but invalid values abort startup instead of being silently swallowed.
            Config = Bind<T>(builder.Configuration);
        }

        /// <summary>Sealed: configurable modules implement the two-argument overload,
        /// because the configuration is bound anew for every application instance.</summary>
        protected sealed override void Load(ContainerBuilder builder) => Load(builder, Config);

        protected abstract void Load(ContainerBuilder builder, T config);
        #endregion
    }
}