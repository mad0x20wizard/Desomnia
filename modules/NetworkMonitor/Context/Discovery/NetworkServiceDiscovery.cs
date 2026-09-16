using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Discovery.BuiltIn;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        private static void RegisterServiceDiscovery(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            if (config.AutoDetect.HasFlag(AutoDiscoveryType.SleepProxy))
            {
                var reg = builder.RegisterType<SleepProxyDetector>().As<IDisposable>()
                    .WithParameter(TypedParameter.From(config.AutoTimeout))
                    .SingleInstance()
                    .AsSelf();

                if (config.SleepProxyDiscovery.HasFlag(SleepProxyDiscoveryType.Eager))
                {
                    reg.As<IServiceDiscovery>();
                }

                else if (config.SleepProxyDiscovery.HasFlag(SleepProxyDiscoveryType.Lazy))
                {
                    // SleepProxyDetector has to be called first, so that the HandoffService can find any proxy
                    reg.As<INetworkService>().WithPriority(-1);

                    if (config.SleepProxyDiscovery.HasFlag(SleepProxyDiscoveryType.Fast))
                    {
                        reg.OnActivated(args => args.Instance.UseFirstSleepProxy = true);
                    }
                }
            }

            builder.RegisterType<RemoteHostServiceDetector>()
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
        }

        internal async Task DiscoverServices()
        {
            Logger.LogDebug("Discovering services...");

            foreach (var discovery in Scope.Resolve<IEnumerable<IServiceDiscovery>>())
            {
                await discovery.DiscoverServices(Network);
            }

            foreach (var ctx in _hostContexts.Where(ctx => ctx.Auto.HasFlag(AutoDiscoveryType.Service)))
            {
                if (ctx.Watch is null)
                    continue;

                foreach (var discoveryHost in Scope.Resolve<IEnumerable<IWatchedServiceDiscovery>>())
                {
                    await discoveryHost.DiscoverServices(ctx.Watch);
                }
            }
        }
    }

    public partial class NetworkHostContext
    {
        internal void CreateStaticWatchedServices(IEnumerable<WatchedServiceInfo> services)
        {
            foreach (var info in services)
            {
                CreateWatchedService(info, new(ServiceFlags.Static));
            }
        }

        public NetworkServiceContext CreateWatchedService(WatchedServiceInfo info, ServiceOptions options = default)
        {
            return CreateWatchedService<NetworkServiceContext>(
                new TypedParameter(typeof(WatchedServiceInfo), info), 
                new TypedParameter(typeof(ServiceOptions), options));
        }

        public T CreateWatchedService<T>(params Parameter[] parameters) where T : NetworkServiceContext
        {
            var ctx = Scope.Resolve<T>(parameters);

            try
            {
                Host.AddService(ctx.Service, ctx.Options);

                Logger.LogHostServiceAdded(Host, ctx.Service);
            }
            catch (Exception) // service probably already exists
            {
                ctx.Dispose();

                throw;
            }

            Watch?.StartTracking(ctx.Watch);

            /*
             * The service's traffic shapes were already registered while its scope was built,
             * i.e. before the watch was tracked. Now that the service can influence the host's
             * block-by-default state, the capture filter has to be re-evaluated.
             */
            Scope.ResolveOptional<BerkeleyPacketFilter>()?.Refresh();

            ctx.Scope.CurrentScopeEnding += (sender, args) =>
            {
                Host.RemoveService(ctx.Service);

                Logger.LogHostServiceRemoved(Host, ctx.Service);

                Watch?.StopTracking(ctx.Watch);

                _serviceContexts.Remove(ctx);
            };

            _serviceContexts.Add(ctx);

            return ctx;
        }
    }
}
