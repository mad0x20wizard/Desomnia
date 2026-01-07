using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Demand.Filter
{
    internal class DemandRouterFilter : IDemandPacketFilter
    {
        public required ILogger<DemandRouterFilter> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        public required ReachabilityService Reachability { private get; init; }

        bool IPacketFilter.ShouldFilter(EthernetPacket packet)
        {
            if (SentByRouter(packet) is NetworkRouter router)
            {
                if (ShouldAllow(router, packet) && !packet.IsIPUnicast() && packet.FindTargetIPAddress() is IPAddress ip)
                {
                    throw new IPUnicastNeededException(ip); // respond to ARP/NDP request
                }

                if (!router.Options.AllowWake)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldAllow(NetworkRouter router, EthernetPacket packet)
        {
            /*
             * Has the user opted out of the router filtering?
             * 
             * Otherwise we won't allow the router to trigger a direct wake,
             * unless another Address (probably from some remote system) has 
             * actually triggered the wake request.
             */
            if (router.Options.AllowWake || router.Options.AllowWakeByProxy)
            {
                return true; // no further checks required
            }

            /*
             * Then we check if we deal with a MagicPacket packet
             * and if the user has made an exception to allow MagicPacket.
             */
            else if (router.Options.AllowWakeOnLAN && packet.IsMagicPacket())
            {
                return true;
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

        private NetworkRouter? SentByRouter(EthernetPacket packet)
        {
            return Network.OfType<NetworkRouter>().Where(router => router.HasAddress(ip: packet.FindSourceIPAddress())).FirstOrDefault();
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
