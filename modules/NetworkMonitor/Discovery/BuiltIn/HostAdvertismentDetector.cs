using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using MadWizard.Desomnia.Network.Services;
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
            _hosts.GetOrAdd(host, []).Add(family); // only remember which hosts should receive Address address updates
        }

        void INetworkService.ProcessPacket(EthernetPacket packet)
        {
            // ARP
            if (packet.PayloadPacket is ArpPacket arp)
            {
                if (Network[arp.SenderProtocolAddress] is NetworkHost hostByIP)
                {
                    AdvertiseAddress(hostByIP, arp.SenderProtocolAddress);
                }

                /**
                 * Some routers do Proxy ARP for their VPN clients, so we need to ignore these IPs then.
                 */
                else if (Network[arp.SenderHardwareAddress] is NetworkHost hostByMAC && hostByMAC is not NetworkRouter)
                {
                    if (!arp.SenderProtocolAddress.IsEmpty() && !arp.SenderProtocolAddress.IsAPIPA())
                    {
                        AdvertiseAddress(hostByMAC, arp.SenderProtocolAddress);
                    }
                }
            }

            // NDP
            else if (packet.Extract<NdpPacket>() is NdpNeighborAdvertisementPacket ndpNeighbor)
            {
                if (Network[ndpNeighbor.TargetAddress] is NetworkHost hostByIP)
                {
                    AdvertiseAddress(hostByIP, ndpNeighbor.TargetAddress);
                }
                else if (Network[ndpNeighbor.FindSourcePhysicalAddress()!] is NetworkHost hostByMAC)
                {
                    if (!ndpNeighbor.TargetAddress.IsEmpty())
                    {
                        AdvertiseAddress(hostByMAC, ndpNeighbor.TargetAddress);
                    }
                }
            }
        }

        private void AdvertiseAddress(NetworkHost host, IPAddress ip, TimeSpan? lifetime = null)
        {
            Reachability.Notify(host, ip);

            if (_hosts.TryGetValue(host, out var families) && families.Contains(ip.AddressFamily))
            {
                if (!host.HasAddress(ip:ip))
                {
                    Logger.LogDebug("Host '{HostName}' advertised unknown {Family} address '{IPAddress}'",
                        host.Name, ip.ToFamilyName(), ip);

                    host.AddAddress(ip, lifetime ?? options.Latency);
                }
            }
        }
    }
}
