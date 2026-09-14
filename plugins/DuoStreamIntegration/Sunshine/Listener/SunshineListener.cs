using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using WindowsFirewallHelper;
using WindowsFirewallHelper.FirewallRules;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Listener
{
    internal class SunshineListener(SunshineService service) : NetworkServiceWatch(service)
    {
        public required ILogger<SunshineListener> Logger { private get; init; }

        public required IFirewall       Firewall    { private get; init; }
        private         IFirewallRule?  Rule        { get; set; }

        public new SunshineService Service => service;

        CancellationTokenSource? _cancelSource;

        private void ConfigureFirewall()
        {
            if (Rule is null)
            {
                Rule = Firewall.CreatePortRule(
                    name:       $"DesomniaDuoInstance:{service.Port}",
                    profiles:   FirewallProfiles.Domain | FirewallProfiles.Private | FirewallProfiles.Public,
                    action:     FirewallAction.Allow,
                    protocol:   FirewallProtocol.TCP,
                    portNumber: service.Port
                );

                if (Rule is FirewallWASRule wasRule)
                {
                    string? exePath = Environment.ProcessPath
                        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                        ?? null;

                    wasRule.ApplicationName = exePath;
                    wasRule.Description = $"Needed to automatically start/stop the Duo instance with port {service.Port}.";
                }

                Firewall.Rules.Add(Rule);

                Logger.LogDebug("Configured Windows Firewall to allow incoming connection. Rule name = '{name}'", Rule.Name);
            }
        }

        public async void WaitForClient()
        {
            using TcpListener listener = new(IPAddress.Any, service.HTTP.Port);

            try
            {
                ConfigureFirewall();

                listener.Start();

                Logger.LogTrace($"Listening for incoming connections on port {service.HTTP.Port}...");

                _cancelSource = new();

                while (_cancelSource != null)

                    try
                    {
                        using TcpClient client = await listener.AcceptTcpClientAsync(_cancelSource.Token);

                        if (client.Client.RemoteEndPoint is IPEndPoint remote)
                        {
                            if (IPAddress.IsLoopback(remote.Address))
                            {
                                Logger.LogTrace("Ignored local connection attempt from {endpoint}", remote);

                                client.Close();

                                continue; // ignore local connections
                            }

                            Logger.LogDebug("Accepted incoming connection attempt from {endpoint}", remote);

                            TriggerDemand();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                Logger.LogTrace("Stopped to listen for incoming connections on port {port}.", service.HTTP.Port);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                Logger.LogWarning("Could not listen for incoming connections on port {port}: Address already in use.", service.HTTP.Port);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
            {
                Logger.LogError("Could not listen for incoming connections on port {port}: Access denied.", service.HTTP.Port);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not listen for incoming connections on port {port}: Unknown error.", service.HTTP.Port);
            }
        }

        public void StopWaiting()
        {
            _cancelSource?.Cancel();
            _cancelSource = null;
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            // TODO check tcp connection attempts

            if (service.IsWaitingForClient)
            {
                if (service.HasClientConnected)
                    ReportNetworkTraffic();
            }
            else
            {
                WaitForClient(); // long term safety net
            }

            if (HadThresholdTraffic(interval, out long bytes))
            {
                yield return new NetworkServiceUsage(service, bytes);
            }
        }

        private void UnconfigureFirewall()
        {
            if (Rule is not null && Rule.Name is string name)
            {
                try
                {
                    Firewall.Rules.Remove(Rule);

                    Logger.LogDebug("Removed Windows Firewall rule '{name}'.", name);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not remove Windows Firewall rule '{name}'.", name);
                }
                finally
                {
                    Rule = null;
                }
            }
        }

        public override void Dispose()
        {
            StopWaiting();

            UnconfigureFirewall();

            base.Dispose();
        }
    }
}
