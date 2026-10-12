using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Timers;

namespace MadWizard.Desomnia.Network
{
    public class NetworkJanitor : IDisposable
    {
        private static readonly TimeSpan HostExpirationGracePeriod = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RangeExpirationGracePeriod = TimeSpan.FromSeconds(5);

        public required ILogger<NetworkJanitor> Logger { private get; init; }

        public required NetworkSegment Network { private get; init; }

        private readonly ConcurrentDictionary<NetworkHost, NetworkHostContext> _sweepableHosts = [];
        private readonly ConcurrentDictionary<NetworkHost, byte> _watchedHosts = [];
        private readonly ConcurrentDictionary<NetworkHostRange, byte> _watchedRanges = [];
        private readonly AsyncAutoResetEvent _cleanupChanged = new(false);

        private CancellationTokenSource? _sweepCancellation;
        private Task _sweeping = Task.CompletedTask;
        private bool _disposed;

        public void StartSweeping()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sweepCancellation != null)
                throw new Exception("Sweeping already started.");

            Network.HostAdded += Host_Added;
            Network.HostRemoved += Host_Removed;
            foreach (var host in Network)
                WatchHost(host);

            _sweepCancellation = new();
            _sweeping = SweepUntilStopped(_sweepCancellation.Token);
        }

        private async Task SweepUntilStopped(CancellationToken stoppingToken)
        {
            using var timer = new ScheduledTimer();
            timer.Elapsed += (_, _) => _cleanupChanged.Set();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using (await Network.Mutex.LockAsync(stoppingToken))
                    {
                        timer.Stop();
                        timer.DueTime = Sweep(DateTime.Now);
                        if (timer.DueTime is not null)
                            timer.Start();
                    }

                    // Changes only wake this worker. Cleanup and deadline calculation always
                    // share the network mutex with packet processing, including on renewal.
                    await _cleanupChanged.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private void WatchHost(NetworkHost host)
        {
            if (_watchedHosts.TryAdd(host, 0))
                host.Changed += Resource_CleanupChanged;
        }

        private void Host_Added(object? sender, NetworkHostEventArgs args)
        {
            WatchHost(args.Host);
            _cleanupChanged.Set();
        }

        private void Host_Removed(object? sender, NetworkHostEventArgs args)
        {
            if (_watchedHosts.TryRemove(args.Host, out _))
                args.Host.Changed -= Resource_CleanupChanged;
            _sweepableHosts.TryRemove(args.Host, out _);
            _cleanupChanged.Set();
        }

        internal void WatchRange(NetworkHostRange range)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchedRanges.TryAdd(range, 0))
                range.Changed += Resource_CleanupChanged;
            Resource_CleanupChanged(range, EventArgs.Empty);
        }

        private void Resource_CleanupChanged(object? sender, EventArgs args)
        {
            if (sender is NetworkHostRange { IsDisposed: true } range && _watchedRanges.TryRemove(range, out _))
                range.Changed -= Resource_CleanupChanged;
            _cleanupChanged.Set();
        }

        internal void MakeHostEligibleForSweeping(NetworkHostContext context)
        {
            if (_sweepableHosts.TryAdd(context.Host, context))
            {
                context.Scope.CurrentScopeEnding += (_, _) =>
                {
                    _sweepableHosts.TryRemove(context.Host, out _);
                    _cleanupChanged.Set();
                };
            }
            _cleanupChanged.Set();
        }

        // Caller holds Network.Mutex. The returned deadline already includes the grace for each resource type.
        internal DateTime? Sweep(DateTime now)
        {
            DateTime? next = null;

            void Schedule(DateTime deadline)
            {
                if (next is null || deadline < next)
                    next = deadline;
            }

            foreach (var host in Network)
            {
                foreach (var ip in host.IPAddresses.ToArray())
                {
                    if (host.ShouldAddressExpire(ip, out var expires))
                    {
                        var deadline = CleanupTime(expires, HostExpirationGracePeriod);
                        if (deadline > now) Schedule(deadline);
                        else if (host.RemoveAddress(ip, true))
                        {
                            using var scope = Logger.BeginHostScope(host);
                            Logger.LogHostAddressRemoved(host, ip);
                        }
                    }
                }

                foreach (var service in host.Services.ToArray())
                {
                    if (host.ShouldServiceExpire(service, out var expires))
                    {
                        var deadline = CleanupTime(expires, HostExpirationGracePeriod);
                        if (deadline > now) Schedule(deadline);
                        else if (host.RemoveService(service, true))
                        {
                            using var scope = Logger.BeginHostScope(host);
                            Logger.LogHostServiceRemoved(host, service, true);
                        }
                    }
                }
            }

            foreach (var range in _watchedRanges.Keys)
            {
                foreach (var address in range.ToArray())
                {
                    if (range.ShouldAddressRangeExpire(address, out var expires))
                    {
                        var deadline = CleanupTime(expires, RangeExpirationGracePeriod);
                        if (deadline > now) Schedule(deadline);
                        else range.RemoveAddressRange(address, expires);
                    }
                }
            }

            foreach (var ctx in _sweepableHosts.Values.ToArray())
            {
                if (ctx.Host.IsReferencedByFilterRules || ctx.Host.Services.Any())
                    continue;

                if (ctx.Host is NetworkRouter { ValidUntil: DateTime validUntil } && validUntil > now)
                {
                    Schedule(validUntil);
                    continue;
                }

                ctx.Dispose();
                _sweepableHosts.TryRemove(ctx.Host, out _);
            }

            return next;
        }

        private static DateTime CleanupTime(DateTime expires, TimeSpan grace) =>
            expires > DateTime.MaxValue - grace ? DateTime.MaxValue : expires + grace;

        private void StopWatchingHosts()
        {
            Network.HostAdded -= Host_Added;
            Network.HostRemoved -= Host_Removed;
            foreach (var host in _watchedHosts.Keys)
            {
                host.Changed -= Resource_CleanupChanged;
                _watchedHosts.TryRemove(host, out _);
            }
        }

        public async Task StopSweeping()
        {
            if (_sweepCancellation is not { } cancellation) return;
            cancellation.Cancel();
            try { await _sweeping; }
            finally
            {
                StopWatchingHosts();
                _sweepCancellation = null;
                cancellation.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_sweepCancellation is { } cancellation)
            {
                cancellation.Cancel();
                // Disposal may run under the network mutex; never block on a worker waiting for it.
                _ = _sweeping.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
                _sweepCancellation = null;
            }
            StopWatchingHosts();
            foreach (var range in _watchedRanges.Keys)
                range.Changed -= Resource_CleanupChanged;
            _watchedRanges.Clear();
            _sweepableHosts.Clear();
        }
    }
}
