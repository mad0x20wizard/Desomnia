using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class HostAdvertismentDetector(DiscoveryOptions options) : INetworkService, IIPAddressDiscovery
    {
        public required ILogger<HostAdvertismentDetector> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        public required ReachabilityService Reachability { private get; init; }

        readonly ConcurrentDictionary<NetworkHost, HashSet<AddressFamily>> _hosts = [];

        async Task IIPAddressDiscovery.DiscoverIPAddresses(NetworkHost host, AddressFamily family)
        {
            _hosts.GetOrAdd(host, []).Add(family); // only remember which hosts should receive IP address updates
        }

        void INetworkService.ProcessCapture(INetworkCapture capture)
        {
            // ARP
            if (capture.FindPacket<ArpPacket>() is ArpPacket arp)
            {
                if (Network[arp.SenderProtocolAddress] is NetworkHost hostByIP)
                {
                    AdvertiseAddress(hostByIP, capture.SourcePhysicalAddress, arp.SenderProtocolAddress);
                }

                /**
                 * Some routers do Proxy ARP for their VPN clients, so we need to ignore these IPs then.
                 */
                else if (Network[arp.SenderHardwareAddress] is NetworkHost hostByMAC && hostByMAC is not NetworkRouter)
                {
                    if (!arp.SenderProtocolAddress.IsEmpty()) // TODO APIPA? && !arp.SenderProtocolAddress.IsAPIPA())
                    {
                        AdvertiseAddress(hostByMAC, capture.SourcePhysicalAddress, arp.SenderProtocolAddress);
                    }
                }
            }

            // NDP
            else if (capture.FindPacket<NdpPacket>() is NdpNeighborAdvertisementPacket ndpNeighbor)
            {
                if (Network[ndpNeighbor.TargetAddress] is NetworkHost hostByIP)
                {
                    AdvertiseAddress(hostByIP, capture.SourcePhysicalAddress, ndpNeighbor.TargetAddress);
                }
                else if (Network[ndpNeighbor.FindSourcePhysicalAddress()!] is NetworkHost hostByMAC)
                {
                    if (!ndpNeighbor.TargetAddress.IsEmpty())
                    {
                        AdvertiseAddress(hostByMAC, capture.SourcePhysicalAddress, ndpNeighbor.TargetAddress);
                    }
                }
            }
        }

        private void AdvertiseAddress(NetworkHost host, PhysicalAddress mac, IPAddress ip, TimeSpan? lifetime = null)
        {
            Reachability.Notify(host, mac, new IPEndPoint(ip, 0));

            if (_hosts.TryGetValue(host, out var families) && families.Contains(ip.AddressFamily))
            {
                using var scope = Logger.BeginHostScope(host);

                if (host.AddAddress(ip, new(lifetime ?? options.Refresh)))
                {
                    Logger.LogHostAddressAdded(host, ip);
                }
            }
        }
    }
}
