using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;
using MadWizard.Desomnia.Network.Context.Parameters;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using Microsoft.Extensions.Logging;
using NLog;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Context
{
    public class NetworkHostContext : FilterContext
    {
        public NetworkHost          Host    { get; private init; }
        public NetworkHostWatch?    Watch   { get; private init; }

        public ILogger<NetworkHostContext> Logger => field ??= Scope.Resolve<ILogger<NetworkHostContext>>();

        public IEnumerable<NetworkServiceWatch> ServiceWatches => Scope.Resolve<IEnumerable<NetworkServiceWatch>>();

        // Host
        public NetworkHostContext(ILifetimeScope parent, NetworkMonitorConfig networkConfig, NetworkHostInfo config) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, builder =>
            {
                RegisterHost(builder, config);
            });

            Host = ConfigureHost(networkConfig, config).Result;
        }

        // Router
        public NetworkHostContext(ILifetimeScope parent, NetworkMonitorConfig networkConfig, NetworkRouterInfo config) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, builder =>
            {
                builder.RegisterType<NetworkRouter>().As<NetworkHost>()
                    .WithParameter(new TypedParameter(typeof(string), config.Name))
                    .WithParameter(NetworkHostsParameter.FindBy([.. config.VPNClient.Select(h => h.Name)]))
                    .WithParameter(TypedParameter.From(config.Options))
                    .SingleInstance()
                    .AsSelf();
            });

            Host = ConfigureHost(networkConfig, config).Result;
        }

        // LocalHost
        public NetworkHostContext(ILifetimeScope parent, NetworkMonitorConfig networkConfig, LocalHostInfo config) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, builder =>
            {
                builder.RegisterType<LocalHost>().As<NetworkHost>()
                    .SingleInstance()
                    .AsSelf();

                RegisterServices(builder, config.Services);

                RegisterHostFilters(builder, config.HostFilterRule);
                RegisterHostRangeFilters(builder, config.HostRangeFilterRule);

                builder.RegisterType<LocalHostWatch>().As<NetworkHostWatch>()
                    .WithParameter(TypedParameter.From(config.MakeDemandOptions(networkConfig)))
                    .SingleInstance()
                    .AsSelf();
            });

            Host = ConfigureLocalHost(networkConfig, config).Result;
            Watch = Scope.Resolve<LocalHostWatch>();

            Watch.Threshold = config.MinTraffic;
        }

        // LocalVirtualHost
        public NetworkHostContext(ILifetimeScope parent, NetworkMonitorConfig networkConfig, LocalVirtualHostInfo config, IVirtualMachine vm) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, builder =>
            {
                builder.RegisterType<VirtualNetworkHost>().As<NetworkHost>()
                    .WithParameter(new TypedParameter(typeof(string), config.Name))
                    .WithParameter(new LocalHostParameter<NetworkHost>())
                    .WithProperty(TypedParameter.From(vm.Address))
                    .SingleInstance()
                    .AsSelf();

                RegisterServices(builder, config.Services);
                RegisterFilters(builder, config);

                builder.RegisterType<LocalVirtualHostWatch>().As<NetworkHostWatch>()
                    .WithParameter(TypedParameter.From(vm))
                    .WithParameter(TypedParameter.From(config.MakeDemandOptions(networkConfig)))
                    .SingleInstance()
                    .AsSelf();
            });

            Host = ConfigureHost(networkConfig, config).Result;
            Watch = ConfigureWatch(networkConfig, config);
        }

        // RemoteHost
        public NetworkHostContext(ILifetimeScope parent, NetworkMonitorConfig networkConfig, RemotePhysicalHostInfo config) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, builder =>
            {
                RegisterHost(builder, config);
                RegisterServices(builder, config.Services);
                RegisterFilters(builder, config);

                builder.RegisterType<RemoteHostWatch>().As<NetworkHostWatch>()
                    .WithParameter(TypedParameter.From(config.MakeDemandOptions(networkConfig)))
                    .WithParameter(TypedParameter.From(config.MakePingOptions(networkConfig)))
                    .WithParameter(TypedParameter.From(config.MakeWakeOptions(networkConfig)))
                    .SingleInstance()
                    .AsSelf();
            });

            Host = ConfigureHost(networkConfig, config).Result;
            Watch = ConfigureWatch(networkConfig, config);
        }

        // RemoteVirtualHost
        public NetworkHostContext(ILifetimeScope parent, NetworkMonitorConfig networkConfig, RemotePhysicalHostInfo hostConfig, RemoteVirtualHostInfo config) : base(parent)
        {
            Scope = parent.BeginLifetimeScope(MatchingScopeLifetimeTags.NetworkHostLifetimeScopeTag, builder =>
            {
                builder.RegisterType<VirtualNetworkHost>().As<NetworkHost>()
                    .WithParameter(new TypedParameter(typeof(string), config.Name))
                    .WithParameter(new NetworkHostParameter<NetworkHost>(hostConfig.Name))
                    .SingleInstance()
                    .AsSelf();

                RegisterServices(builder, config.Services);
                RegisterFilters(builder, config);

                builder.RegisterType<RemoteVirtualHostWatch>().As<NetworkHostWatch>()
                    .WithParameter(NetworkHostWatchParameter<RemoteHostWatch>.FindByHostName(hostConfig.Name))
                    .WithParameter(TypedParameter.From(config.MakeDemandOptions(networkConfig)))
                    .WithParameter(TypedParameter.From(config.MakePingOptions(networkConfig)))
                    .WithParameter(TypedParameter.From(config.MakeWakeOptions(networkConfig)))
                    .SingleInstance()
                    .AsSelf();

            });

            Host = ConfigureHost(networkConfig, config).Result;
            Watch = ConfigureWatch(networkConfig, config);
        }

        private void RegisterHost(ContainerBuilder builder, NetworkHostInfo config)
        {
            builder.RegisterType<NetworkHost>().As<NetworkHost>()
                .WithParameter(new TypedParameter(typeof(string), config.Name))
                .SingleInstance()
                .AsSelf();
        }

        private void RegisterServices(ContainerBuilder builder, IEnumerable<ServiceInfo> services)
        {
            foreach (var info in services)
            {
                builder.RegisterType<TransportNetworkService>().As<NetworkService>()
                    .WithParameter(TypedParameter.From(info.Name))
                    .WithParameter(TypedParameter.From(info.TransportService))
                    .WithProperty(TypedParameter.From(info.ServiceName))
                    .SingleInstance()
                    .AsSelf();

                var watch = builder.RegisterType<NetworkServiceWatch>()
                    .WithParameter(NetworkServiceParameter.FindByName(info.Name))
                    .WithProperty(TypedParameter.From(info.MakeKnockOptions()))
                    .WithProperty(TypedParameter.From(info.MinTraffic))
                    .SingleInstance()
                    .AsSelf();

                watch.OnActivated(args =>
                {
                    args.Instance.AddEventAction(nameof(NetworkServiceWatch.Demand), info.OnDemand);
                    args.Instance.AddEventAction(nameof(NetworkServiceWatch.Idle), info.OnIdle);
                });

                RegisterServiceFilter(builder, info);
            }
        }

        private async Task<LocalHost> ConfigureLocalHost(NetworkMonitorConfig configNetwork, LocalHostInfo config)
        {
            Logger.LogDebug("Configuring localhost:");

            var host = Scope.Resolve<LocalHost>();

            Logger.LogDebug("{family} '{address}'", "MAC", host.PhysicalAddress?.ToHexString());

            foreach (var ip in host.IPAddresses)
                Logger.LogDebug("{family} '{address}'", ip.ToFamilyName(), ip);

            // Configure traffic filters

            if (config.Services.Any())
            {
                if (host.IPv4Addresses.Any())
                    Scope.UseTrafficType(new IPv4TrafficType());
                if (host.IPv6Addresses.Any())
                    Scope.UseTrafficType(new IPv6TrafficType());
            }

            return host;
        }

        private async Task<NetworkHost> ConfigureHost(NetworkMonitorConfig configNetwork, NetworkHostInfo config)
        {
            Logger.LogDebug("Configuring host '{name}':", config.Name);

            var host = Scope.Resolve<NetworkHost>();

            // Configure hostname
            if (config.HostName != null)
            {
                host.HostName = config.HostName;
            }

            var autoDetect = config.AutoDetect ?? configNetwork.AutoDetect;

            // Configure static MAC address
            if ((host.PhysicalAddress ??= config.MAC) is PhysicalAddress mac)
            {
                Logger.LogHostPhysicalAddressChanged(host, mac);
            }

            // Dynamically resolve MAC address
            if (host.PhysicalAddress == null && Scope.ResolveOptional<IPhysicalAddressDiscovery>() is IPhysicalAddressDiscovery discoverMac)
            {
                if (autoDetect.HasFlag(AutoDiscoveryType.MAC))
                {
                    await discoverMac.DiscoverAddress(host);
                }
            }

            // Configure static Address addresses
            foreach (var ip in config.IPAddresses)
            {
                if (host.AddAddress(ip))
                {
                    Logger.LogHostAddressAdded(host, ip);
                }
            }

            // Dynamically resolve Address addresses
            if (Scope.ResolveOptional<IIPAddressDiscovery>() is IIPAddressDiscovery discoverIP)
            {
                if (autoDetect.HasFlag(AutoDiscoveryType.IPv4))
                    await discoverIP.DiscoverIPAddresses(host, AddressFamily.InterNetwork);
                if (autoDetect.HasFlag(AutoDiscoveryType.IPv6))
                    await discoverIP.DiscoverIPAddresses(host, AddressFamily.InterNetworkV6);
            }

            if (!host.IPAddresses.Any())
            {
                Logger.LogWarning("Host \"{name}\" has no IP addresses configured.", config.Name);
            }

            // Configure traffic filters
            if (autoDetect.HasFlag(AutoDiscoveryType.IPv4) || host.IPv4Addresses.Any())
                Scope.UseTrafficType(new IPv4TrafficType());
            if (autoDetect.HasFlag(AutoDiscoveryType.IPv6) || host.IPv6Addresses.Any())
                Scope.UseTrafficType(new IPv6TrafficType());

            return host;
        }

        private NetworkHostWatch ConfigureWatch(NetworkMonitorConfig configNetwork, WatchedHostInfo config)
        {
            var watch = Scope.Resolve<NetworkHostWatch>();

            watch.Threshold = config.MinTraffic;

            if (watch is HostDemandWatch)
            {
                watch.AddEventAction(nameof(HostDemandWatch.Demand), config.OnDemand);
                watch.AddEventAction(nameof(HostDemandWatch.Idle), config.OnIdle);

                watch.AddEventAction(nameof(HostDemandWatch.Started), config.OnStart);
                watch.AddEventAction(nameof(HostDemandWatch.Suspended), config.OnSuspend);
                watch.AddEventAction(nameof(HostDemandWatch.Stopped), config.OnStop);

                watch.AddEventAction(nameof(HostDemandWatch.MagicPacket), config.OnMagicPacket);

                if (watch.Host.PhysicalAddress is null && watch.Host.IsInLocalRange())
                {
                    Logger.LogWarning("Host '{name}' has no MAC address configured.", config.Name);
                }
            }

            return watch;
        }

        internal void TrackDynamicServices()
        {
            Watch?.TrackingStarted += (sender, args) => AddDynamicTrafficFilters(Host, args.Inspectable.Service);
            Watch?.TrackingStopped += (sender, args) => RemoveDynamicTrafficFilters(Host, args.Inspectable.Service);
        }
    }
}
