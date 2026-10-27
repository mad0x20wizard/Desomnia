using MadWizard.Desomnia.Network.Address;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Reachability;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    internal abstract class PhysicalAddressDetector<T> : IPhysicalAddressDiscovery, INetworkService
    {
        private readonly object _pendingLock = new();
        private readonly List<PendingDiscovery> _pending = [];

        public abstract ILogger<T> Logger { protected get; init; }

        public required NetworkDevice   Device { private get; init; }
        public required NetworkSegment  Network { private get; init; }

        public required ReachabilityService Reachability { protected get; init; }

        public required DiscoveryOptions Options { private get; init; }

        async Task IPhysicalAddressDiscovery.DiscoverAddress(NetworkHost host)
        {
            var ips = host.IPAddresses.Where(ip => ip.AddressFamily == Family && Network.LocalRange.Contains(ip)).ToArray();

            if (host.PhysicalAddress is null && ips.Any())
            {
                var pending = new PendingDiscovery(host, ips);

                Logger.LogDebug("Host '{HostName}' has no MAC address configured, try to resolve dynamically...", host.Name);

                lock (_pendingLock)
                    _pending.Add(pending);

                try
                {
                    foreach (var ip in ips)
                    {
                        if (!pending.Completion.Task.IsCompleted)
                            SendRequest(ip);
                    }

                    await pending.Completion.Task.WaitAsync(Options.Timeout);
                }
                catch (TimeoutException)
                {
                    // An unanswered probe leaves discovery to the other providers.
                }
                catch (OperationCanceledException)
                {
                    // Monitoring stopped while discovery was pending.
                }
                finally
                {
                    lock (_pendingLock)
                        _pending.Remove(pending);
                }
            }
        }

        void INetworkService.ProcessCapture(INetworkCapture packet)
        {
            lock (_pendingLock)
            {
                if (_pending.Count == 0 || AnalyzeCapture(packet) is not AddressMapping mapping)
                    return;

                foreach (var pending in _pending)
                {
                    if (!pending.Addresses.Contains(mapping.IPAddress))
                        continue;

                    if (pending.Host.PhysicalAddress is null)
                    {
                        pending.Host.PhysicalAddress = mapping.PhysicalAddress;
                        Logger.LogHostPhysicalAddressChanged(pending.Host, mapping.PhysicalAddress);
                    }
                    pending.Completion.TrySetResult();
                }
            }
        }

        Task INetworkService.Suspend()
        {
            lock (_pendingLock)
            {
                foreach (var pending in _pending)
                    pending.Completion.TrySetCanceled();
                _pending.Clear();
            }
            return Task.CompletedTask;
        }

        Task INetworkService.Shutdown(NetworkShutdownReason reason) => ((INetworkService)this).Suspend();

        private sealed record PendingDiscovery(NetworkHost Host, IPAddress[] Addresses)
        {
            public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        protected abstract AddressFamily Family { get; }

        protected abstract void SendRequest(IPAddress ip);

        protected abstract AddressMapping? AnalyzeCapture(INetworkCapture packet);

    }
}
