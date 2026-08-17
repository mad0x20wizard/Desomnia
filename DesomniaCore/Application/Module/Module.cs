using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog.Config;

namespace MadWizard.Desomnia
{
    public abstract class Module : Autofac.Module
    {
        protected internal virtual void ConfigureLogging(ISetupExtensionsBuilder builder) { }

        protected internal virtual void Build(HostApplicationBuilder builder) { }

        /// <summary>
        /// Registers services whose lifetime is the machine, not any one effective
        /// configuration. Called exactly once per module, when the persistent container is
        /// built at boot; the resulting persistent container is shared by every
        /// configuration rebuild and disposed only when the whole application stops.
        /// Because this runs once, no configuration is available here — persistent
        /// services must work without one. Stateful services holding OS resources are
        /// registered as singletons; stateless helpers (matchers, environment conditions)
        /// may stay transient, as long as they are not disposable — disposed-per-resolve
        /// does not exist in a machine-lifetime container (checked at build where the
        /// registration reveals its type; delegate-created instances are checked when a
        /// bridged resolve surfaces one). Per-scope lifetimes are rejected, and
        /// open-generic registrations are not bridged — register closed types.
        /// Every registration is bridged into each application container by the
        /// <see cref="FrameworkContainerBridge"/>, so the application resolves and uses
        /// the services but never disposes them.
        /// </summary>
        protected internal virtual void LoadOnce(ContainerBuilder builder) { }

        /// <summary>
        /// The configuration-aware overload of <see cref="LoadOnce(ContainerBuilder)"/>: it
        /// receives the persistent configuration — the <c>&lt;?global key="value"?&gt;</c>
        /// directives of the configuration file, read before any container is built — so a
        /// module can bind its own process-bound options (each binds the sections it needs,
        /// ignoring the rest) and register accordingly. The configuration may be empty
        /// (persistent configuration is optional); bind against defaults. The default
        /// implementation forwards to the argument-less overload.
        /// </summary>
        protected internal virtual void LoadOnce(ContainerBuilder builder, IConfiguration config) => LoadOnce(builder);
    }
}
