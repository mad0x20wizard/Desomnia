using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    internal static class NetworkExt
    {
        public static bool HasBeenSeen(this RemoteHostWatch host, TimeSpan? duration = null)
        {
            return host.LastSeen != null && (DateTime.Now - host.LastSeen) < (duration ?? host.PingOptions.Timeout);
        }

        public static bool HasBeenUnseen(this RemoteHostWatch host, TimeSpan? duration = null)
        {
            return host.LastUnseen != null && (DateTime.Now - host.LastUnseen) < (duration ?? host.PingOptions.Timeout);
        }

        public static bool HasBeenWokenSince(this RemoteHostWatch host, TimeSpan duration)
        {
            return host.LastWoken != null && (DateTime.Now - host.LastWoken) < duration;
        }

        public static bool IsDefaultGateway(this NetworkRouter router, INetworkInterface ni)
        {
            return router.IPAddresses.Any(ip => ni.Gateways.Any(gateway => gateway.Address.RemoveScopeId().Equals(ip)));
        }

        public static bool HasSeenVPNClients(this NetworkRouter router, TimeSpan? duration = null)
        {
            return router.LastSeenVPN != null && (DateTime.Now - router.LastSeenVPN) < (duration ?? router.Options.VPNTimeout);
        }

        public static bool HasSentPacket(this NetworkDevice device, EthernetPacket packet)
        {
            return device.PhysicalAddress.Equals(packet.SourceHardwareAddress);
        }

        public static NetworkHost? FindHostByIP(this NetworkSegment network, IEnumerable<IPAddress> addresses)
        {
            foreach (IPAddress ip in addresses)
            {
                if (network[ip] is NetworkHost host)
                    return host;
            }

            return null;
        }

        public static string ToHostTypeString(this NetworkHost host)
        {
            switch (host)
            {
                case LocalHost:
                    return "local host";
                case NetworkRouter:
                    return "router";
                case VirtualNetworkHost:
                    return "virtual host";
                default:
                    return "host";
            }
        }

        public static bool IsInLocalRange(this NetworkHost host)
        {
            return host.IPAddresses.Any(host.Network.LocalRange.Contains);
        }
    }
}
