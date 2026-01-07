using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using System.Net;

namespace MadWizard.Desomnia.Network
{
    public class NetworkJanitor(SweepOptions options)
    {
        public required NetworkSegment Network { private get; init; }

        private HashSet<NetworkHost> _sweepableHosts = [];

        private CancellationTokenSource? _sweepCancellation;

        public async void StartSweeping()
        {
            if (_sweepCancellation != null)
                throw new Exception("Sweeping already started.");

            var stoppingToken = (_sweepCancellation = new()).Token;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(options.Frequency, stoppingToken);

                    using (await Network.Mutex.LockAsync(stoppingToken))
                    {
                        SweepHostAddresses(Network);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        internal void MakeHostEligibleForSweeping(NetworkHost host)
        {
            _sweepableHosts.Add(host);
        }

        internal void SweepHostAddresses(NetworkSegment network)
        {
            List<NetworkHost>? emptyHosts = null;

            foreach (var host in network)
            {
                List<IPAddress>? removeIPs = null;
                foreach (var ip in host.IPAddresses)
                {
                    if (host.ShouldAddressExpire(ip, out var expires))
                    {
                        if (DateTime.Now - expires > options.Delay)
                        {
                            (removeIPs ??= []).Add(ip);
                        }
                    }
                }

                foreach (var adr in removeIPs ?? Enumerable.Empty<IPAddress>())
                {
                    host.RemoveAddress(adr, true);
                }

                if (!host.IPAddresses.Any())
                {
                    (emptyHosts ??= []).Add(host);
                }
            }

            foreach (var host in (emptyHosts ?? Enumerable.Empty<NetworkHost>()).Where(_sweepableHosts.Contains))
            {
                network.RemoveHost(host);

                _sweepableHosts.Remove(host);
            }
        }

        public void StopSweeping()
        {
            if (_sweepCancellation == null)
                throw new Exception("Sweeping not yet started.");

            _sweepCancellation.Cancel();
            _sweepCancellation = null;
        }
    }
}
