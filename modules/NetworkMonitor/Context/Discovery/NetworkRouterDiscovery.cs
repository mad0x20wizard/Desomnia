using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Discovery.BuiltIn;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        private static void RegisterRouterDiscovery(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            // Router/Options-Discovery
            builder.RegisterType<DefaultGatewayDetector>().WithPriority(1)
                .WithParameter(TypedParameter.From(config.AutoDetect))
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<RouterAdvertismentDetector>().WithPriority(2)
                .WithParameter(TypedParameter.From(config.AutoDetect))
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }

        public async Task<T> CreateRouter<T>(NetworkRouterInfo info, params Parameter[] parameters) where T : NetworkRouterContext
        {
            foreach (var configVPNClient in info.VPNClient)
            {
                configVPNClient.AutoDetect ??= Config.AutoDetect;

                configVPNClient.AutoDetect &= ~AutoDiscoveryType.MAC; // this can never work

                CreateHost(new TypedParameter(typeof(NetworkHostInfo), configVPNClient));
            }

            // Bind by the declared base type (router contexts take a NetworkRouterInfo) as well as
            // the runtime type — TypedParameter matches exactly, so a derived info like
            // FRITZBoxRouterInfo would otherwise not satisfy a `NetworkRouterInfo config` parameter.
            var ctx = CreateHost<T>([ .. parameters,
                new TypedParameter(typeof(NetworkMonitorConfig), Config),
                new TypedParameter(typeof(NetworkRouterInfo), info), // TODO ugly
                new TypedParameter(info.GetType(), info),
            ]);

            await ctx.DiscoverAddresses();

            return ctx;
        }

        public async Task<T> CreateDynamicRouter<T>(NetworkRouterInfo info) where T : NetworkRouterContext
        {
            var ctx = await CreateRouter<T>(info);

            Scope.Resolve<NetworkJanitor>().MakeHostEligibleForSweeping(ctx);

            return ctx;
        }

        internal async Task DiscoverRouters()
        {
            Logger.LogDebug("Discovering routers...");

            // register static routers from the configuration — always
            foreach (var configRouter in Config.Router)
            {
                await CreateRouter<NetworkRouterContext>(configRouter);
            }

            var discoveries = Scope.Resolve<IEnumerable<IRouterDiscovery>>();

            // let discoverers create their statically-configured routers — always
            foreach (var discovery in discoveries)
            {
                await discovery.ConfigureRouters(Network);
            }

            // active router lookup (default gateway, NDP advertisements, DNS-SD) — only on opt-in
            if (Config.AutoDetect.HasFlag(AutoDiscoveryType.Router))
            {
                foreach (var discovery in discoveries)
                {
                    await discovery.DiscoverRouters(Network);
                }
            }
        }
    }
}