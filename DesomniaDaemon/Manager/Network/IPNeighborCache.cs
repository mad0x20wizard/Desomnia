using MadWizard.Desomnia.Network.Device;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Manager
{
    internal class IPNeighborCache : ILocalAddressMapping
    {
        public required ILogger<IPNeighborCache> Logger { private get; init; }

        public required NetworkDevice Device { protected get; init; }

        private string DeviceName => Device.Interface.Name;

        void ILocalAddressMapping.Update(IPAddress ip, PhysicalAddress mac)
        {
            // IMPROVE add [ nud STATE ] ? which state, "permanent" or "reachable"?

            exec($"-family {ip.ToFamilyName()} neigh replace {ip} lladdr {mac.ToPlatformString()} dev {DeviceName}");
        }

        void ILocalAddressMapping.Delete(IPAddress ip)
        {
            exec($"-family {ip.ToFamilyName()} neigh del {ip} dev {DeviceName}");
        }

        private void exec(string arguments)
        {
            System.Diagnostics.Process command = new()
            {
                StartInfo = new()
                {
                    FileName = "ip",
                    Arguments = arguments,

                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    //RedirectStandardInput = true,
                }
            };

            command.Start();
            command.WaitForExit();
            if (command.StandardError.ReadToEnd() is string message && !string.IsNullOrEmpty(message))
            {
                Logger.LogError($"Failed to execute \"ip {arguments}\" – {message.Trim()}");
            }
            else
            {
                Logger.LogTrace($"Executed \"ip {arguments}\"");
            }
        }

    }

    file static class IPAddressExt
    {
        public static string ToFamilyName(this IPAddress ip)
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    return "inet";
                case AddressFamily.InterNetworkV6:
                    return "inet6";

                default:
                    throw new NotSupportedException($"Unsupported address family: {ip.AddressFamily}");
            }
        }
    }

}
