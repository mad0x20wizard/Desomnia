using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Disables and enables network interfaces via "ip link set dev &lt;name&gt; up/down"
    /// (the daemon runs as root; iproute2 is a base package on every supported distribution).
    /// The base's enumeration defaults fit Linux: a downed link stays enumerated, so absence
    /// really means gone — and there is no SSID to answer until an nl80211 lookup exists.
    /// </summary>
    internal sealed class IPNetworkInterfaceManager : NetworkInterfaceManager
    {
        protected override void DisableInterface(INetworkInterface @interface)
        {
            SetState(@interface.Name, up: false);
        }

        protected override void EnableInterface(INetworkInterface @interface)
        {
            SetState(@interface.Name, up: true);
        }

        private void SetState(string name, bool up)
        {
            string state = up ? "up" : "down";

            Logger.LogTrace($"ip link set dev {name} {state}");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ip",
                ArgumentList = { "link", "set", "dev", name, state },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("ip failed to start.");

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();

                throw new InvalidOperationException($"\"ip link set dev {name} {state}\" failed: {error.Trim()}");
            }
        }
    }
}
