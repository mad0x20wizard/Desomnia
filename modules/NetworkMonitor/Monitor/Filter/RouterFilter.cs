using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Filter
{
    internal class RouterFilter : IFilter
    {
        public required ILogger<RouterFilter> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        public required ReachabilityService Reachability { private get; init; }

        bool IFilter.ShouldFilter(IFilterContext context)
        {
            if (FindPeerRouter(context) is NetworkRouter router)
            {
                if (context.Direction == FlowDirection.Inbound)
                {
                    if (context is not IIPFilterContext && ShouldAllow(router))
                    {
                        throw new IPTrafficNeededException(); // respond to ARP/NDP request
                    }
                }

                if (!router.Options.AllowWake)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldAllow(NetworkRouter router)
        {
            /*
             * Has the user opted out of the router filtering?
             * 
             * Otherwise we won't allow the router to trigger a direct wake,
             * unless another Options (probably from some remote system) has 
             * actually triggered the wake request.
             */
            if (router.Options.AllowWake || router.Options.AllowWakeByProxy)
            {
                return true; // no further checks required
            }


            /*
             * Then we check if the user has opted in, to allow VPN clients
             * and whether the router has any VPN clients connected.
             */
            else if (router.Options.AllowWakeByVPNClients && HasAnyVPNClientConnected(router).Result) // this takes some time, TODO: how can we do this asynchronously?
            {
                return true;
            }

            return false;
        }

        private NetworkRouter? FindPeerRouter(IFilterContext context)
        {
            return Network.OfType<NetworkRouter>().FirstOrDefault(router => router.HasAddress(context.RemoteAddress));
        }

        public async Task<bool> HasAnyVPNClientConnected(NetworkRouter router)
        {
            if (!router.HasSeenVPNClients())
            {
                Logger.LogDebug($"Checking router '{router.Name}' for VPN clients...");

                var test = new ReachabilityTest(router.VPNClients.SelectMany(client => client.IPAddresses), router.Options.VPNTimeout);

                try
                {
                    await Reachability.Send(test, useICMP: true); // we must use ICMP, because of potential use of Proxy ARP

                    foreach (var ip in test.Where(ip => test[ip] is not null))
                    {
                        Logger.LogDebug($"VPN client '{router.FindVPNClient(ip)?.Name}' is reachable at {ip}");
                    }

                    router.LastSeenVPN = DateTime.Now;
                }
                catch (HostTimeoutException)
                {
                    return false;
                }
            }

            return router.VPNClients.Any();
        }
    }
}
