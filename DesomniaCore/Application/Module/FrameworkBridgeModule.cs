using Autofac;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia
{
    internal sealed class FrameworkBridgeModule(ILifetimeScope root) : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // bridge the persistent services in first, so registrations that gate on them
            // (e.g. the DisplayMonitor's OnlyIf IDisplayManager) see them at build time. The bridge's
            // export policy keeps the persistent host's framework services (hosting, options, the
            // loop) out of the inner container — it runs its own.
            builder.RegisterSource(new RootContainerBridge(root));

            var logging = root.Resolve<ILoggerFactory>();

            // logging is the exception the policy cannot express, because it is process-global:
            // NLog's LogManager is, so the factory fronting it must be too. Registered after the
            // framework's own (populated from builder.Services) so this one answers, and
            // externally owned so a rebuild disposing this container never touches it.
            builder.RegisterInstance(logging).As<ILoggerFactory>().ExternallyOwned();
        }
    }
}
