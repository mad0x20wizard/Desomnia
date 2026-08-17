using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The options-system entry points of the <see cref="StrictConfigurationBinder"/>: the strict
    /// counterparts of <c>services.Configure&lt;T&gt;(IConfiguration)</c> and
    /// <c>OptionsBuilder&lt;T&gt;.Bind(IConfiguration)</c>. The wiring is the stock one — a
    /// configure action bound to the section plus the section's change token, so an
    /// <see cref="IOptionsMonitor{TOptions}"/> re-binds when the configuration reloads — but the
    /// binding is the modules' (see <c>ConfigurableModule.Bind</c>): invalid values throw a
    /// <see cref="ConfigurationValueException"/> instead of being silently skipped, the value
    /// variations apply ("90s", "5min", ISO 8601 durations, dashed enum names, "|"-separated
    /// flags), and non-public setters bind. Unknown keys stay tolerated (the open format).
    ///
    /// <para>Options are bound lazily and per reload: a bad value surfaces where the options are
    /// read (<c>Value</c>/<c>CurrentValue</c>) — at boot that fails the build like any bad
    /// configuration; on a reload the change callback faults (logged as an unobserved task
    /// exception) and the reads keep throwing until the file is fixed. This does not replace the
    /// modules' own configuration binding; it is for the options-shaped consumers, typically the
    /// root configuration in <c>Module.BuildOnce</c>.</para>
    /// </summary>
    public static class StrictOptionsExtensions
    {
        const string DynamicCodeWarningMessage = "Binding strongly typed objects to configuration values requires generating dynamic code at runtime, for example instantiating generic types.";
        const string TrimmingWarningMessage = "TOptions's dependent types may have their members trimmed. Ensure all required members are preserved.";

        /// <summary>
        /// Registers a configuration instance which <typeparamref name="TOptions"/> will bind
        /// against through the strict binder — the strict counterpart of
        /// <c>Configure&lt;TOptions&gt;(IConfiguration)</c>.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static IServiceCollection ConfigureStrict<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
            this IServiceCollection services, IConfiguration config)
            where TOptions : class
            => services.ConfigureStrict<TOptions>(Microsoft.Extensions.Options.Options.DefaultName, config, configureBinder: null);

        /// <summary>
        /// Registers a configuration instance which <typeparamref name="TOptions"/> will bind
        /// against through the strict binder, with binder options on top of the strict defaults.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static IServiceCollection ConfigureStrict<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
            this IServiceCollection services, IConfiguration config, Action<BinderOptions>? configureBinder)
            where TOptions : class
            => services.ConfigureStrict<TOptions>(Microsoft.Extensions.Options.Options.DefaultName, config, configureBinder);

        /// <summary>
        /// Registers a configuration instance which the named <typeparamref name="TOptions"/> will
        /// bind against through the strict binder. <paramref name="configureBinder"/> runs on top
        /// of the strict defaults (<see cref="BinderOptions.BindNonPublicProperties"/> on, like the
        /// modules' binding), so it may also turn them off.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static IServiceCollection ConfigureStrict<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
            this IServiceCollection services, string? name, IConfiguration config, Action<BinderOptions>? configureBinder)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(config);

            services.AddOptions();

            // the same two registrations the stock Configure(IConfiguration) makes: the section's
            // change token drives the monitor's re-bind, the configure action does the binding
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(new ConfigurationChangeTokenSource<TOptions>(name, config));

            return services.AddSingleton<IConfigureOptions<TOptions>>(new ConfigureNamedOptions<TOptions>(name,
                options => StrictConfigurationBinder.Bind(config, options, binder =>
                {
                    binder.BindNonPublicProperties = true; // the modules' convention (ConfigurableModule.Bind)

                    configureBinder?.Invoke(binder);
                })));
        }

        /// <summary>
        /// Binds the options to the configuration through the strict binder and follows the
        /// configuration's reload token — the strict counterpart of
        /// <c>OptionsBuilder&lt;TOptions&gt;.Bind(IConfiguration)</c>. Composes with the rest of the
        /// builder (<c>Validate</c>, <c>PostConfigure</c>, ...).
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static OptionsBuilder<TOptions> BindStrict<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
            this OptionsBuilder<TOptions> builder, IConfiguration config, Action<BinderOptions>? configureBinder = null)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.ConfigureStrict<TOptions>(builder.Name, config, configureBinder);

            return builder;
        }
    }
}
