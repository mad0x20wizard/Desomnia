using System.Net.Sockets;

namespace System.Net.NetworkInformation
{
    internal static class IPAddressExt
    {
        public static IPAddress LinkLocalMulticast = IPAddress.Parse("ff02::1");
        public static IPAddress LinkLocalRouterMulticast = IPAddress.Parse("ff02::2");

        public static IPAddress DeriveIPv6SolicitedNodeMulticastAddress(this IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();

            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetworkV6:
                    if (ip.IsIPv6Multicast)
                        throw new ArgumentException("Is a multicast address.");

                    // IPv6 solicitated node multicast address
                    return IPAddress.Parse($"FF02::1:FF{bytes[13]:X2}:{bytes[14]:X2}{bytes[15]:X2}");

                default:
                    throw new NotSupportedException($"Unsupported address family: {ip.AddressFamily}");
            }
        }

        public static PhysicalAddress DeriveLayer2MulticastAddress(this IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();

            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    throw new NotImplementedException("IPv4 multicast address derivation not implemented.");

                case AddressFamily.InterNetworkV6:
                    if (!ip.IsIPv6Multicast)
                        throw new ArgumentException("Not a multicast address.");

                    // multicast MAC address
                    return PhysicalAddress.Parse($"33:33:{bytes[12]:X2}:{bytes[13]:X2}:{bytes[14]:X2}:{bytes[15]:X2}");

                default:
                    throw new NotSupportedException($"Unsupported address family: {ip.AddressFamily}");
            }
        }

        public static bool IsEmpty(this IPAddress ip)
        {
            return ip.Equals(IPAddress.Any);
        }

        public static bool IsAPIPA(this IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();

            // ShouldAllow if it's IPv4 and starts with 169.254
            return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
        }

        public static bool IsInSameSubnet(this IPAddress localAddress, IPAddress remoteAddress, int prefixLength)
        {
            var localBytes = localAddress.GetAddressBytes();
            var remoteBytes = remoteAddress.GetAddressBytes();

            if (localBytes.Length != remoteBytes.Length)
                return false;

            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (localBytes[i] != remoteBytes[i])
                    return false;
            }

            if (remainingBits > 0)
            {
                byte mask = (byte)~(0xFF >> remainingBits);
                if ((localBytes[fullBytes] & mask) != (remoteBytes[fullBytes] & mask))
                    return false;
            }

            return true;
        }

        public static string ToFamilyName(this IPAddress ip)
        {
            return ip.AddressFamily.ToFriendlyName();
        }

        public static string ToFriendlyName(this AddressFamily family)
        {
            return family == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
        }

        public static IPAddress RemoveScopeId(this IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId != 0)
            {
                ip.ScopeId = 0; // Reset the scope ID for IPv6 addresses
            }

            return ip;
        }

        public static void RemoveScopeIds(this IEnumerable<IPAddress> ips)
        {
            foreach (var ip in ips)
            {
                ip.RemoveScopeId();
            }
        }

        public static async Task<string?> LookupName(this IPAddress ip)
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip);

                return entry.HostName;
            }
            catch
            {
                return null; // ignore DNS resolution errors
            }
        }
    }
}
