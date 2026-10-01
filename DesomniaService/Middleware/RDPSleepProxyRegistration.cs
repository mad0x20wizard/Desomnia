using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Middleware;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.SleepProxy.Registration;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Net;
using System.Security;

namespace MadWizard.Desomnia.Session.Middleware
{
    public sealed class RDPSleepProxyRegistration : SleepProxyServiceRegistration
    {
        const string REG_TerminalServerPath = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
        const string REG_PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

        protected override IEnumerable<ProxyServiceInfo> RegisterProxyServices(ResolveRequestContext context, LocalHostWatch watch)
        {
            if (watch.Host is LocalHost && DeterminePort(context) is ushort port)
            {
                yield return new ProxyServiceInfo(watch.AdvertiseOptions)
                {
                    Name = "RDP",
                    ServiceName = "rdp", // use "ms-wbt-server" ??

                    Protocol = IPProtocol.TCP,
                    Port = port,
                };
            }
        }

        private static ushort? DeterminePort(ResolveRequestContext context)
        {
            try
            {
                using (var policy = Registry.LocalMachine.OpenSubKey(REG_PolicyPath))
                {
                    if (policy?["fDenyTSConnections"] is not object denyConnections) // Group Policy takes precedence over the local setting when present.
                    {
                        using var terminalServer = Registry.LocalMachine.OpenSubKey(REG_TerminalServerPath);

                        denyConnections = terminalServer?["fDenyTSConnections"] ?? throw new FormatException("fDenyTSConnections is missing");
                    }

                    if (denyConnections is not int disabled || disabled is not (0 or 1))
                        throw new FormatException("fDenyTSConnections is invalid");
                    else if (disabled == 1)
                        return null;
                }

                using (var listener = Registry.LocalMachine.OpenSubKey(REG_TerminalServerPath + @"\WinStations\RDP-Tcp"))
                {
                    if (listener?.GetValue("PortNumber") is not int port || port is < 1 or > ushort.MaxValue)
                        throw new FormatException("PortNumber is missing or invalid");

                    return (ushort)port;
                }
            }
            catch (FormatException ex)
            {
                context.Resolve<ILogger<RDPSleepProxyRegistration>>()
                    .LogWarning(ex, "Skipping RDP sleep proxy registration: {Message}", ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                context.Resolve<ILogger<RDPSleepProxyRegistration>>()
                    .LogWarning(ex, "Skipping RDP sleep proxy registration: unable to read the RDP registry settings.");
            }

            return null;
        }
    }
}
