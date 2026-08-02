using Autofac;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Discovery;
using MadWizard.Desomnia.Network.Discovery.BuiltIn;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Context
{
    public partial class NetworkContext
    {
        private void RegisterAddressDiscovery(ContainerBuilder builder, NetworkMonitorConfig config)
        {
            // MAC-Discovery
            builder.RegisterType<ARPPhysicalAddressDetector>().WithPriority(2)
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<NDPPhysicalAddressDetector>().WithPriority(2)
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            // Host/IP-Discovery
            builder.RegisterType<DNSIPAddressDetector>().WithPriority(1)
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();
            builder.RegisterType<HostAdvertismentDetector>().WithPriority(1)
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            // The detector passively confirms wakes from DHCP BootRequests (client port 68 -> server port 67),
            // so this traffic has to be captured.
            builder.RegisterType<DHCPRequestDetector>()
                .WithParameter(TypedParameter.From(config.MakeAutoDiscoveryOptions()))
                .AsImplementedInterfaces()
                .SingleInstance()
                .AsSelf();

            RegisterTrafficFilter(builder, new UDPTrafficType(67));
        }

        internal async Task DiscoverAddresses()
        {
            var nonRouterContexts = _hostContexts.Where(ctx => ctx.Host is not NetworkRouter);

            if (Config.AutoParallel)
            {
                Logger.LogDebug("Discovering addresses (in parallel)...");

                var tasks = nonRouterContexts.Select(ctx => ctx.DiscoverAddresses());

                await Task.WhenAll(tasks);
            }
            else
            {
                Logger.LogDebug("Discovering addresses...");

                foreach (var ctx in nonRouterContexts)
                {
                    await ctx.DiscoverAddresses();
                }
            }
        }
    }

    public partial class NetworkHostContext
    {
        internal async Task DiscoverAddresses()
        {
            using var scope = Logger.BeginHostScope(Host);

            if (Auto != AutoDiscoveryType.Nothing)
            {
                // Dynamically resolve IP addresses
                foreach (var discoverIP in Scope.Resolve<IEnumerable<IIPAddressDiscovery>>())
                {
                    if (Auto.HasFlag(AutoDiscoveryType.IPv4))
                        await discoverIP.DiscoverIPAddresses(Host, AddressFamily.InterNetwork);
                    if (Auto.HasFlag(AutoDiscoveryType.IPv6))
                        await discoverIP.DiscoverIPAddresses(Host, AddressFamily.InterNetworkV6);
                }

                // Dynamically resolve MAC address
                foreach (var discoverMAC in Scope.Resolve<IEnumerable<IPhysicalAddressDiscovery>>())
                {
                    if (Auto.HasFlag(AutoDiscoveryType.MAC))
                        await discoverMAC.DiscoverAddress(Host);
                }
            }

            ValidateAddresses();
        }

        private void ValidateAddresses()
        {
            using var scope = Logger.BeginHostScope(Host);

            if (!Host.IPAddresses.Any())
            {
                Logger.LogWarning("Host \"{name}\" has no IP addresses configured.", Host.Name);
            }

            if (Watch is HostDemandWatch && Watch.Host is not VirtualNetworkHost)
            {
                if (Host.PhysicalAddress is null && Host.IsInLocalRange())
                {
                    Logger.LogWarning("Host '{name}' has no MAC address configured.", Host.Name);
                }
            }

            // Configure traffic filters TODO maybe move into dynamic callback
            if (Auto.HasFlag(AutoDiscoveryType.IPv4) || Host.IPv4Addresses.Any())
                Scope.UseTrafficType(new IPv4TrafficType());
            if (Auto.HasFlag(AutoDiscoveryType.IPv6) || Host.IPv6Addresses.Any())
                Scope.UseTrafficType(new IPv6TrafficType());
        }
    }
}
