using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network
{
    public class NetworkJanitor(SweepOptions options)
    {
        public required ILogger<NetworkJanitor> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        private HashSet<NetworkHostContext> _sweepableHosts = [];

        private CancellationTokenSource? _sweepCancellation;

        private Task _sweeping = Task.CompletedTask;

        public void StartSweeping()
        {
            if (_sweepCancellation != null)
                throw new Exception("Sweeping already started.");

            _sweepCancellation = new();
            _sweeping = SweepUntilStopped(_sweepCancellation.Token);
        }

        private async Task SweepUntilStopped(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(options.Frequency, stoppingToken);

                    using (await Network.Mutex.LockAsync(stoppingToken))
                    {
                        SweepHostAddresses(Network);
                        SweepHostServices(Network);
                        SweepHosts(Network);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        internal void MakeHostEligibleForSweeping(NetworkHostContext host)
        {
            _sweepableHosts.Add(host);
        }

        internal void SweepHostAddresses(NetworkSegment network)
        {
            foreach (var host in network)
            {
                foreach (var ip in host.IPAddresses.ToArray())
                {
                    if (host.ShouldAddressExpire(ip, out var expires))
                    {
                        if (DateTime.Now - expires > options.Delay)
                        {
                            if (host.RemoveAddress(ip, true))
                            {
                                using var scope = Logger.BeginHostScope(host);

                                Logger.LogHostAddressRemoved(host, ip);
                            }
                        }
                    }
                }
            }
        }

        internal void SweepHostServices(NetworkSegment network)
        {
            foreach (var host in network)
            {
                foreach (var service in host.Services.ToArray())
                {
                    if (host.ShouldServiceExpire(service, out var expires))
                    {
                        if (DateTime.Now - expires > options.Delay)
                        {
                            if (host.RemoveService(service, true))
                            {
                                using var scope = Logger.BeginHostScope(host);

                                Logger.LogHostServiceRemoved(host, service, true);
                            }
                        }
                    }
                }
            }
        }

        internal void SweepHosts(NetworkSegment network)
        {
            foreach (var ctx in _sweepableHosts.ToArray())
            {
                if (ctx.Host.FilterRefCount > 0)
                    continue;

                // A dynamically discovered host (e.g. a Sleep Proxy) is only retired once every service
                // it advertised has expired and been swept; until then we keep it in the map.
                if (ctx.Host.Services.Any())
                    continue;

                if (ctx.Host is NetworkRouter router)
                {
                    if (DateTime.Now < router.ValidUntil) // null will not match
                        continue;
                }

                ctx.Dispose();

                _sweepableHosts.Remove(ctx);
            }
        }

        public async Task StopSweeping()
        {
            if (_sweepCancellation is not { } cancellation) return;
            cancellation.Cancel();
            try { await _sweeping; }
            finally
            {
                _sweepCancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
