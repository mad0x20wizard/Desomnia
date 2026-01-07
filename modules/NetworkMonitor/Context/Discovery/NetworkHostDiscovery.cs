using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Discovery.BuiltIn;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        private static void RegisterDiscovery(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            // MAC-Discovery
            builder.RegisterType<ARPPhysicalAddressDetector>()
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<NDPPhysicalAddressDetector>()
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            // Host/Address-Discovery
            builder.RegisterType<DNSIPAddressDetector>()
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<HostAdvertismentDetector>()
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            // Router/Address-Discovery
            builder.RegisterType<DefaultGatewayDetector>()
                .WithParameter(TypedParameter.From(config.AutoDetect))
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<RouterAdvertismentDetector>()
                .WithParameter(TypedParameter.From(config.AutoDetect))
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }

        internal async Task DiscoverRouters()
        {
            Logger.LogDebug("Discovering routers...");

            // register static routers
            foreach (var configRouter in Config.Router)
            {
                foreach (var configVPNClient in configRouter.VPNClient)
                {
                    CreateHost(new TypedParameter(typeof(NetworkHostInfo), configVPNClient));
                }

                CreateHost(new TypedParameter(typeof(NetworkRouterInfo), configRouter));
            }

            // register dynamic routers
            if (Config.AutoDetect.HasFlag(AutoDiscoveryType.Router))
            {
                if (Scope.ResolveOptional<IRouterDiscovery>() is IRouterDiscovery discovery)
                {
                    await discovery.DiscoverRouters(Network);
                }
            }
        }

        internal async Task DiscoverHosts()
        {
            Logger.LogDebug("Discovering network hosts...");

            CreateLocalHost();

            foreach (var configHost in Config.Host)
            {
                CreateHost(new TypedParameter(typeof(NetworkHostInfo), configHost));
            }

            foreach (var configRange in Config.Ranges)
            {
                foreach (var configHostInRange in configRange.Host)
                {
                    CreateHost(new TypedParameter(typeof(NetworkHostInfo), configHostInRange));
                }
            }

            foreach (var configHost in Config.RemoteHost)
            {
                CreateHost(new TypedParameter(typeof(RemotePhysicalHostInfo), configHost));

                foreach (var configHostVirtual in configHost.VirtualHost)
                {
                    CreateHost(
                        new TypedParameter(typeof(RemotePhysicalHostInfo), configHost),
                        new TypedParameter(typeof(RemoteVirtualHostInfo), configHostVirtual)
                    );
                }
            }
        }

        private void CreateLocalHost()
        {
            LocalHostInfo configHost;
            if (Config.LocalHost is not null)
            {
                if (Config.Service.Any() || Config.HTTPService.Any() || Config.VirtualHost.Any())
                    throw new Exception("You have to specify the configuration of local services and virtual hosts on the LocalHost node.");

                configHost = Config.LocalHost;
            }
            else
            {
                Config.HostFilterRule.Clear(); // don't register these filters twice

                configHost = Config;
            }

            CreateHost(new TypedParameter(typeof(LocalHostInfo), configHost));

            foreach (var configHostVirtual in configHost.VirtualHost)
            {
                if (VMManager[configHostVirtual.Name!] is IVirtualMachine vm)
                {
                    CreateHost(
                        new TypedParameter(typeof(LocalVirtualHostInfo), configHostVirtual),
                        new TypedParameter(typeof(IVirtualMachine), vm)
                    );
                }
            }
        }

        internal void CreateHost(params Parameter[] parameters)
        {
            try
            {
                var context = Scope.Resolve<NetworkHostContext>(parameters);

                ConfigureNetworkMonitorWith(context);

                _hostContexts.Add(context);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to create host context:");
            }
        }

        public NetworkHostContext CreateDynamicHost(params Parameter[] parameters)
        {
            var context = Scope.Resolve<NetworkHostContext>(parameters);

            ConfigureNetworkMonitorWith(context);

            Scope.Resolve<NetworkJanitor>().MakeHostEligibleForSweeping(context.Host);

            return context;
        }

        private void ConfigureNetworkMonitorWith(NetworkHostContext context)
        {
            Network.AddHost(context.Host);

            if (context.Watch is NetworkHostWatch watch)
            {
                foreach (var serviceWatch in context.ServiceWatches)
                {
                    watch.StartTracking(serviceWatch);
                }

                this.Monitor.StartTracking(watch);

                context.TrackDynamicServices();
            }
        }
    }
}
