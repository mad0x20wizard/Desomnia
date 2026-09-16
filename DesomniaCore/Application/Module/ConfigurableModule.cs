using Autofac;
using MadWizard.Desomnia.Application.Registry;
using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MadWizard.Desomnia
{
    public abstract class ConfigurableModule : Module
    {
        private IConfiguration? _rootConfig;

        #region Versioning
        /// <summary>
        /// The product-wide configuration format version: bumped whenever ANY module introduces
        /// an incompatible change to the file format (a file declares ONE version, as the root
        /// element's <c>version</c> attribute — optionally mirrored in the &lt;?config?&gt;
        /// header — so the format is versioned as a whole, not per module). A file may stay at
        /// an OLDER version for as long as no loaded module demands a newer one (see
        /// <see cref="MinVersion"/> and <see cref="ModuleRegistry"/>).
        /// The history lives in docs/concepts/version.rst — version 2: the
        /// DuoStreamIntegration plugin's element.
        /// </summary>
        public const uint LATEST_VERSION = 2;

        /// <summary>
        /// The format version in which this module last introduced an incompatible change, i.e.
        /// the oldest file version it accepts WITHOUT migration. A module raises it to N when it
        /// ships a change in version N together with the migration step for N (see
        /// <c>Configuration.Xml.IXConfigurationMigration</c>).
        /// </summary>
        protected internal virtual uint MinVersion => 1;

        /// <summary>
        /// The newest format version this module accepts. Normally NOT overridden: modules and
        /// plugins then accept every future version (as a virtual property of the core assembly,
        /// an old plugin binary sees the new core's <see cref="LATEST_VERSION"/>). Only a plugin
        /// that must be strict overrides it with a literal — the whole product is then limited
        /// to that version.
        /// </summary>
        protected internal virtual uint MaxVersion => LATEST_VERSION;
        #endregion

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
        /// <see cref="BuildOnce"/>'s <c>builder.Configuration</c>, i.e. the <c>&lt;?system key="value"?&gt;</c>
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
