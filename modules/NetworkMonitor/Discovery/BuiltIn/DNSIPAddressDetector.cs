using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Timers;

using Timer = System.Timers.Timer;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal class DNSIPAddressDetector(DiscoveryOptions options) : IIPAddressDiscovery, IDisposable
    {
        public required ILogger<DNSIPAddressDetector> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        readonly ConcurrentDictionary<NetworkHost, HashSet<AddressFamily>> _hosts = [];

        Timer?                  _autoTimer;
        CancellationTokenSource _autoCancellation = new();

        public void MaybeStartTimer()
        {
            if (options.Refresh is TimeSpan latency && _autoTimer == null)
            {
                _autoTimer = new Timer(latency.TotalMilliseconds);
                _autoTimer.Elapsed += RefreshAddresses;
                _autoTimer.AutoReset = false;
                _autoTimer.Start();
            }
        }

        public async Task DiscoverIPAddresses(NetworkHost host, AddressFamily family)
        {
            _hosts.GetOrAdd(host, []).Add(family);

            foreach (var ip in await FindIPAddresses(host, family, CancellationToken.None)) using (await Network.Mutex.LockAsync())
            {
                if (host.AddAddress(ip, new(options.Refresh)))
                {
                    Logger.LogHostAddressAdded(host, ip);
                }
            }

            MaybeStartTimer();
        }

        private async void RefreshAddresses(object? sender, ElapsedEventArgs args)
        {
            _autoTimer?.Stop();

            try
            {
                Logger.LogDebug("Refreshing auto-configured IP addresses...");

                foreach (var entry in _hosts)
                {
                    var host = entry.Key;

                    foreach (var family in entry.Value)
                    {
                        foreach (var ip in await FindIPAddresses(host, family, _autoCancellation.Token)) using (await Network.Mutex.LockAsync())
                        {
                            using var scope = Logger.BeginHostScope(host);

                            if (host.AddAddress(ip, new(options.Refresh)))
                            {
                                Logger.LogHostAddressAdded(host, ip);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while refreshing auto-configured IP addresses: {Message}", ex.Message);
            }
            finally
            {
                if (!_autoCancellation.IsCancellationRequested)
                {
                    _autoTimer?.Start();
                }
            }
        }

        private async Task<IEnumerable<IPAddress>> FindIPAddresses(NetworkHost host, AddressFamily family, CancellationToken token)
        {
            const string errorMsg = "Failed to resolve {AddressFamily} addresses for host '{HostName}' -> {Error}";

            IPAddress[] addresses = [];

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token).WithTimeout(options.Timeout);

                try
                {
                    addresses = await Dns.GetHostAddressesAsync(host.HostName, family, cts.Token);
                }
                catch (SocketException ex)
                {
                    switch (ex.SocketErrorCode)
                    {
                        case SocketError.TryAgain:
                            Logger.LogTrace(errorMsg, family.ToFriendlyName(), host.HostName, "TRY_AGAIN");
                            break;

                        case SocketError.HostNotFound:
                            //Logger.LogDebug(errorMsg, family.ToFriendlyName(), host.HostName, "NOT_FOUND");
                            break;

                        case SocketError.NoData:
                            // that simply means, there are no IP addresses known to the DNS
                            //Logger.LogTrace(errorMsg, family.ToFriendlyName(), host.HostName, "NO_DATA");
                            break;

                        default:
                            Logger.LogError(ex, errorMsg, family.ToFriendlyName(), host.HostName, $"[{ex.SocketErrorCode}]");
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning(errorMsg, family, host.HostName, "TIMEOUT");
            }

            return addresses;
        }

        public void Dispose()
        {
            _autoCancellation?.Cancel();
            _autoTimer?.Stop();
            _autoTimer = null;
        }
    }
}
