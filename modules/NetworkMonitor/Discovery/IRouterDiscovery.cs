using MadWizard.Desomnia.Network.Context;

namespace MadWizard.Desomnia.Network.Discovery
{
    public interface IRouterDiscovery
    {
        /// <summary>
        /// Creates the discoverer's statically-configured routers — the counterpart of the built-in
        /// <c>&lt;Router&gt;</c> config elements, for plugins. Runs unconditionally: declaring a router
        /// in the configuration is intent enough, no <c>autoDetect="Router"</c> required. Optional —
        /// the default does nothing; a plugin overrides it only if it configures routers explicitly.
        /// </summary>
        Task ConfigureRouters(NetworkContext ctx) => Task.CompletedTask;

        /// <summary>
        /// Active router lookup (default gateway, NDP router advertisements, DNS-SD). Runs only when
        /// the network opted into <c>autoDetect="Router"</c>.
        /// </summary>
        Task DiscoverRouters(NetworkContext ctx);
    }
}
