using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using MadWizard.Desomnia.Network.Watch;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace MadWizard.Desomnia.Network.Reachability
{
    public class ReachabilityTest : IIEnumerable<IPAddress>, IDisposable
    {
        readonly Stopwatch _watch = Stopwatch.StartNew();

        readonly SemaphoreSlim _semaphore = new(0);

        protected readonly ConcurrentDictionary<IPAddress, TimeSpan?> _latencyByIP = [];

        public TimeSpan Timeout { get; private init; }
        public TimeSpan Elapsed => _watch.Elapsed;

        public event EventHandler? Finished;

        public ReachabilityTest(IEnumerable<IPAddress> addresses, TimeSpan timeout)
        {
            Timeout = timeout;

            foreach (var address in addresses)
            {
                _latencyByIP[address] = null;
            }
        }

        public TimeSpan? this[IPAddress ip] => _latencyByIP[ip];

        public virtual void NotifyReachable(IPEndPoint endpoint)
        {
            var address = endpoint.Address;
            if (_latencyByIP.TryGetValue(address, out TimeSpan? latency) && latency == null)
            {
                _latencyByIP[address] = _watch.Elapsed;

                _semaphore.Release();
            }
        }

        public async Task<bool> RespondedTimely()
        {
            return await _semaphore.WaitAsync(Timeout);
        }

        IEnumerator<IPAddress> IEnumerable<IPAddress>.GetEnumerator() => _latencyByIP.Keys.GetEnumerator();

        public virtual void Dispose()
        {
            if (_watch.IsRunning)
            {
                _watch.Stop();

                _semaphore.Dispose();

                Finished?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public class HostReachabilityTest : ReachabilityTest
    {
        readonly NetworkHost _host;

        public HostReachabilityTest(RemoteHostWatch watch, TimeSpan? timeout = null) : base(watch.Host.IPAddresses, timeout ?? watch.PingOptions.Timeout)
        {
            _host = watch.Host;
            _host.AddressAdded += Host_AddressAdded;
            _host.AddressRemoved += Host_AddressRemoved;
        }

        private void Host_AddressAdded(object? sender, AddressEventArgs args) => _latencyByIP[args.IPAddress] = null;
        private void Host_AddressRemoved(object? sender, AddressRemovedEventArgs args) => _latencyByIP.Remove(args.IPAddress, out _);

        public override void Dispose()
        {
            _host?.AddressAdded -= Host_AddressAdded;
            _host?.AddressRemoved -= Host_AddressRemoved;

            base.Dispose();
        }
    }

    public class ServiceReachabilityTest(IPServiceEndPoint endpoint, TimeSpan timeout) : ReachabilityTest([endpoint.Address], timeout)
    {
        public IPServiceEndPoint EndPoint => endpoint;

        public override void NotifyReachable(IPEndPoint source)
        {
            if (source is IPServiceEndPoint sourceEndPoint && sourceEndPoint.Service == endpoint.Service)
            {
                base.NotifyReachable(source);
            }
        }
    }
}
