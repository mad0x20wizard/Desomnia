using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Disables and enables network interfaces via "ifconfig &lt;name&gt; up/down" (the daemon
    /// runs as root). A process call is deliberate: variadic libc functions like ioctl cannot
    /// be P/Invoked reliably on Apple Silicon. The base's existence check needs no override —
    /// a downed interface stays in the macOS enumeration, so absence really means gone (dock
    /// USB NICs) — and the daemon has no wireless source, so an SSID stays unanswerable.
    /// </summary>
    internal sealed class NetToolsInterfaceManager : NetworkInterfaceManager
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

            Logger.LogTrace($"ifconfig {name} {state}");

            using var process = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "ifconfig",
                ArgumentList = { name, state },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("ifconfig failed to start.");

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();

                throw new InvalidOperationException($"\"ifconfig {name} {state}\" failed: {error.Trim()}");
            }
        }
    }
}
