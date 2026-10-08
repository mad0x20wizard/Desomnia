using Autofac;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Naming;
using MadWizard.Desomnia.Network.Naming.Browser.Events;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using MadWizard.Desomnia.Network.SleepProxy;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn
{
    /// <summary>
    /// Discovers Bonjour Sleep Proxies (BSP) on the link by browsing DNS-SD for
    /// <c>_sleep-proxy._udp.local</c> through the <see cref="MulticastServiceBrowser"/>. It maps each
    /// resolved <see cref="ServiceInstance"/> onto the network model -- creating an un-watched host for a
    /// previously unknown proxy (reaped by the <see cref="NetworkJanitor"/> once service-less) or merely
    /// adding the service to a known one -- and then tracks the instance's lifecycle events to keep that
    /// model in sync as addresses come and go and until the instance is retired.
    /// </summary>
    internal class SleepProxyDetector(TimeSpan timeout = default) : IServiceDiscovery, INetworkService, IDisposable
    {
        /// <summary>The DNS-SD service type a Bonjour Sleep Proxy advertises itself under (RFC 6763).</summary>
        private static readonly DomainName ServiceDomainName = new("_sleep-proxy", "_udp", "local");

        public required ILogger<SleepProxyDetector> Logger { private get; init; }

        public required NetworkContext Context { private get; init; }
        public required NetworkSegment Network { private get; init; }

        public required MulticastServiceBrowser Browser { private get; init; }

        public bool UseFirstSleepProxy { get; set; } = false;

        readonly ConcurrentDictionary<ServiceInstance, NetworkHostService> _tracked = [];

        readonly CancellationTokenSource _cancellation = new();

        async Task IServiceDiscovery.DiscoverServices(NetworkSegment network) // SleepProxyDiscoverType = eager
        {
            Logger.LogDebug("Discovering sleep proxies...");

            await DiscoverSleepProxiesWithTimeout(timeout); // gather immediate results

            _ = Task.Run(() => DiscoverSleepProxies(_cancellation.Token)); // scan for the long run
        }

        async Task INetworkService.BeforeSuspend() // SleepProxyDiscoverType = lazy
        {
            Logger.LogDebug("Discovering sleep proxies...");

            await DiscoverSleepProxiesWithTimeout(timeout);
        }

        Task INetworkService.Resume()
        {
            foreach (var instance in _tracked.Keys.ToArray())
            {
                Remove(instance, false);
            }
            return Task.CompletedTask;
        }

        private async Task DiscoverSleepProxiesWithTimeout(TimeSpan timeout)
        {
            using var cts = _cancellation.WithTimeout(timeout);

            var found = await DiscoverSleepProxies(cts.Token, UseFirstSleepProxy ? 1 : null);

            if (found == 0)
            {
                Logger.LogWarning("No sleep proxies were found after {Timeout}", timeout);
            }
        }

        /// <summary>
        /// The browse stream lives for the duration of the network context; it must not block startup, so we consume
        /// it on an independent task. The presence (or absence) of a Sleep Proxy has no impact here.
        /// </summary>
        private async Task<int> DiscoverSleepProxies(CancellationToken token, int? max = null)
        {
            int found = 0;

            try
            {
                using var request = Browser.EnumerateInstances(ServiceDomainName, token);

                await foreach (ServiceInstance instance in request) using (Network.Mutex.Lock())
                {
                    found++;

                    if (!_tracked.ContainsKey(instance))
                    {
                        try
                        {
                            Adopt(instance);

                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Could not adopt sleep proxy instance '{InstanceName}'", instance.Name);
                        }
                    }

                    if (found == max) break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not browse for sleep proxies");
            }

            return found;
        }

        /// <summary>Maps a freshly discovered instance onto the network model. Must run under the network mutex.</summary>
        private void Adopt(ServiceInstance instance)
        {
            var metrics = SleepProxyMetrics.ParseInstanceName(instance.Name, out string name);

            Logger.LogDebug("Discovered sleep proxy '{Name}' with metrics '{Metrics}'", name, metrics);

            NetworkHost host = Network[name] ?? Network[instance.HostName, byHostName: true] ?? CreateProxyHost(name, instance.HostName);

            if (host.Services.WithPort(instance.Port) is not TransportNetworkService service)
            {
                service = new SleepProxyService(instance.Port) { Metrics = metrics };

                if (host.AddService(service))
                {
                    using var scope = Logger.BeginHostScope(host);

                    Logger.LogHostServiceAdded(host, service);
                }
            }

            instance.AddressAdded   += ServiceInstance_AddressAdded;
            instance.AddressRemoved += ServiceInstance_AddressRemoved;
            instance.Removed        += ServiceInstance_Removed;

            _tracked[instance] = new(host, service);

            foreach (IPAddress ip in instance.Addresses)
            {
                ServiceInstance_AddressAdded(instance, new(ip));
            }
        }

        private void Remove(ServiceInstance instance, bool expired)
        {
            if (_tracked.Remove(instance, out NetworkHostService tracked))
            {
                instance.AddressAdded -= ServiceInstance_AddressAdded;
                instance.AddressRemoved -= ServiceInstance_AddressRemoved;
                instance.Removed -= ServiceInstance_Removed;

                if (tracked.Host.RemoveService(tracked.Service, expired: expired))
                {
                    using var scope = Logger.BeginHostScope(tracked.Host);

                    Logger.LogHostServiceRemoved(tracked.Host, tracked.Service, expired);
                }

                // if the host was created dynamically and is now service-less, it will be reaped later by the NetworkJanitor
            }
        }

        #region ServiceInstance events
        private void ServiceInstance_AddressAdded(object? sender, ServiceInstanceAddressEventArgs args)
        {
            if (sender is ServiceInstance instance && _tracked.TryGetValue(instance, out NetworkHostService tracked))
                if (tracked.Host.AddAddress(args.Address))
                {
                    using var scope = Logger.BeginHostScope(tracked.Host);

                    Logger.LogHostAddressAdded(tracked.Host, args.Address);
                }
        }

        private void ServiceInstance_AddressRemoved(object? sender, ServiceInstanceAddressRemovedEventArgs args)
        {
            if (sender is ServiceInstance instance && _tracked.TryGetValue(instance, out NetworkHostService tracked))
                if (tracked.Host.RemoveAddress(args.Address, expired: args.HasExpired))
                {
                    using var scope = Logger.BeginHostScope(tracked.Host);

                    Logger.LogHostAddressRemoved(tracked.Host, args.Address, args.HasExpired);
                }
        }

        private void ServiceInstance_Removed(object? sender, ServiceInstanceRemovedEventArgs args)
        {
            if (sender is ServiceInstance instance)
            {
                Remove(instance, args.HasExpired);
            }
        }
        #endregion

        private NetworkHost CreateProxyHost(string name, string hostname)
        {
            var info = new NetworkHostInfo
            {
                Name        = name,
                HostName    = hostname,

                AutoDetect  = AutoDiscoveryType.Nothing,
            };

            return Context.CreateDynamicHost(new TypedParameter(typeof(NetworkHostInfo), info)).Host;
        }

        void IDisposable.Dispose()
        {
            _cancellation.Cancel();
        }
    }
}
