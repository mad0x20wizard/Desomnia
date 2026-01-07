using Autofac;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class DefaultGatewayDetector(AutoDiscoveryType auto) : IRouterDiscovery
    {
        public required ILogger<DefaultGatewayDetector> Logger { private get; init; }

        public required NetworkDevice Device { private get; init; }

        public required NetworkContext NetworkContext { private get; init; }

        async Task IRouterDiscovery.DiscoverRouters(NetworkSegment network)
        {
            var info = new DefaultGatewayInfo(Device.Interface, auto)
            {
                Name = "DefaultGateway",

                AutoDetect = AutoDiscoveryType.None, // don't look for dynamic IPs, because we have no hostname
            };

            if (network.FindHostByIP(info.IPAddresses) is NetworkRouter router)
            {
                foreach (var additionalIP in info.IPAddresses)
                {
                    if (router.AddAddress(additionalIP)) // only add missing addresses
                    {
                        Logger.LogHostAddressAdded(router, additionalIP);
                    }
                }
            }
            else
            {
                await info.TryLookupGatewayName();

                Logger.LogDebug($"Using default gateway");

                NetworkContext.CreateHost(new TypedParameter(typeof(NetworkRouterInfo), info));
            }
        }
    }
}
