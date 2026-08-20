using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog.Config;

namespace MadWizard.Desomnia
{
    public abstract class Module : Autofac.Module
    {
        protected internal virtual void ConfigureLogging(ISetupExtensionsBuilder builder) { }

        /// <summary>
        /// The root-host counterpart of <see cref="Build"/>: called exactly once per module, while
        /// the root (process-lifetime) host is being built at boot — before
        /// <see cref="LoadOnce(ContainerBuilder, IConfiguration)"/> fills its persistent container.
        /// The builder's <see cref="HostApplicationBuilder.Configuration"/> is the root
        /// configuration: the <c>&lt;?system key="value"?&gt;</c> directives of the configuration
        /// file (empty when there are none — root configuration is optional; bind against
        /// defaults). Bind it here for a value the module needs now, or wire it into the options
        /// system so persistent services take <c>IOptions&lt;T&gt;</c> — or <c>IOptionsMonitor&lt;T&gt;</c>,
        /// which follows edits of the file when auto-reload is on:
        /// <c>builder.Services.ConfigureStrict&lt;T&gt;(builder.Configuration.GetSection(...))</c>
        /// (or <c>AddOptions&lt;T&gt;().BindStrict(...)</c>) binds the modules' way — strict, value
        /// variations, non-public setters (see <see cref="Microsoft.Extensions.DependencyInjection.StrictOptionsExtensions"/>);
        /// the stock <c>Configure&lt;T&gt;(IConfiguration)</c> works too, with the stock binder's
        /// lenient conversions. That is the only reload there is: a value read here or in
        /// <c>LoadOnce</c> is the boot-time value for the life of the process, and a changed
        /// directive is never an error.
        /// </summary>
        protected internal virtual void BuildOnce(HostApplicationBuilder builder) { }

        /// <summary>
        /// Registers services whose lifetime is the machine, not any one effective
        /// configuration. Called exactly once per module, when the persistent container is
        /// built at boot; the resulting persistent container is shared by every
        /// configuration rebuild and disposed only when the whole application stops.
        /// Because this runs once, no effective configuration is available here — persistent
        /// services must work without one (the root configuration is another matter: see
        /// <see cref="BuildOnce"/> and the overload below). Stateful services holding OS resources are
        /// registered as singletons; stateless helpers (matchers, environment conditions)
        /// may stay transient, as long as they are not disposable — disposed-per-resolve
        /// does not exist in a machine-lifetime container (checked at build where the
        /// registration reveals its type; delegate-created instances are checked when a
        /// bridged resolve surfaces one). Per-scope lifetimes are rejected, and
        /// open-generic registrations are not bridged — register closed types.
        /// Every registration is bridged into each application container by the
        /// <see cref="RootContainerBridge"/>, so the application resolves and uses
        /// the services but never disposes them.
        /// </summary>
        protected internal virtual void LoadOnce(ContainerBuilder builder) { }

        /// <summary>
        /// Called for every application host the loop builds (one per effective configuration),
        /// before the module's <see cref="Autofac.Module.Load"/> fills its container: the place to
        /// read the effective configuration through <see cref="HostApplicationBuilder.Configuration"/>
        /// and to register hosting-level services (options, hosted services) with the builder.
        /// </summary>
        protected internal virtual void Build(HostApplicationBuilder builder) { }

    }
}
