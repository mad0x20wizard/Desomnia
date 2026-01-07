using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Manager
{
    internal class MacOSNeighborCache : IAddressCache
    {
        public required ILogger<MacOSNeighborCache> Logger { private get; init; }

        void IAddressCache.Update(IPAddress ip, PhysicalAddress mac)
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    arp($"-d {ip}");
                    arp($"-s {ip} {mac.ToPlatformString()}");
                    break;

                case AddressFamily.InterNetworkV6:
                    ndp($"-d {ip}");
                    ndp($"-s {ip} {mac.ToPlatformString()}");
                    break;

                default:
                    throw new NotSupportedException($"Address family {ip.AddressFamily} is not supported.");
            }
        }

        void IAddressCache.Delete(IPAddress ip)
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    arp($"-d {ip}");
                    break;
                case AddressFamily.InterNetworkV6:
                    ndp($"-d {ip}");
                    break;

                default:
                    throw new NotSupportedException($"Address family {ip.AddressFamily} is not supported.");
            }
        }

        private void arp(string arguments)
        {
            Process command = new()
            {
                StartInfo = new()
                {
                    FileName = "arp",
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
                Logger.LogError($"Failed to execute \"arp {arguments}\" – {message.Trim()}");
            }
            else
            {
                Logger.LogTrace($"Executed \"arp {arguments}\"");
            }
        }

        private void ndp(string arguments)
        {
            Process command = new()
            {
                StartInfo = new()
                {
                    FileName = "ndp",
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
                Logger.LogError($"Failed to execute \"ndp {arguments}\" – {message.Trim()}");
            }
            else
            {
                Logger.LogTrace($"Executed \"ndp {arguments}\"");
            }
        }

    }
}
