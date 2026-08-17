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
        protected internal virtual XDocument MigrateConfiguration(XDocument configuration, uint version) => configuration;

        protected internal virtual void ConfigureConfigurationSource(ExtendedXmlConfigurationSource source) { }

        static protected T Bind<T>(IConfiguration configuration)
        {
            // the strict binder returns null only for a configuration with no keys at all
            // (invalid values throw instead) - a legitimately empty persistent configuration
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
    }
}