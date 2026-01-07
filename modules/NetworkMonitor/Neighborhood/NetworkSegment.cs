using Autofac.Features.Indexed;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkSegment : IIEnumerable<NetworkHost>
    {
        public required ILogger<NetworkSegment> Logger { protected get; init; }

        public AsyncLock Mutex { get; } = new();

        public required NetworkDevice Device { private get; init; }

        public NetworkRouter? DefaultGateway => this.OfType<NetworkRouter>().FirstOrDefault(r => r.IsDefaultGateway(Device.Interface));

        public required LocalNetworkRange LocalRange { get; init; }
        public required IIndex<string, NetworkHostRange> Ranges { get; init; }

        public event EventHandler<NetworkHostEventArgs>? HostAdded;
        public event EventHandler<NetworkHostEventArgs>? HostRemoved;

        readonly ConcurrentDictionary<string, NetworkHost> _hosts = [];

        readonly MemoryCache _cacheHostName = new(new MemoryCacheOptions());

        public NetworkHost? this[string name]
        {
            get => _hosts.TryGetValue(name, out var host) ? host : null;
        }

        public NetworkHost? this[IPAddress? ip]
        {
            get => this.FirstOrDefault(h => h.HasAddress(ip: ip));
        }

        public NetworkHost? this[PhysicalAddress? mac]
        {
            get => this.FirstOrDefault(h => h.HasAddress(mac: mac));
        }

        public void AddHost(NetworkHost host)
        {
            if (!_hosts.ContainsKey(host.Name))
            {
                _hosts[host.Name] = host;

                HostAdded?.Invoke(this, new(host));
            }
            else
                throw new ArgumentException($"Host '{host.Name}' already exists on network '{Device.Interface.Name}'.");
        }

        public void RememberHostName(object key, string name, TimeSpan duration)
        {
            _cacheHostName.Set(key, name, duration);
        }

        public void RemoveHost(NetworkHost host)
        {
            if (_hosts.TryRemove(host.Name, out var removed))
            {
                HostRemoved?.Invoke(this, new(removed));
            }
        }

        public bool IsInLocalZone(string domainName)
        {
            return domainName.Contains(Device.Interface.GetIPProperties().DnsSuffix);
        }

        public async Task<string?> LookupHostName(PhysicalAddress? mac, IPAddress? ip)
        {
            if (((object?)ip ?? mac) is not object key)
                return null;

            if (_cacheHostName.TryGetValue(key, out string? name)) // do we have cached name?
                return name;

            foreach (var router in this.OfType<NetworkRouter>()) // host may be VPN client
            {
                if (router.HasAddress(mac) && router.FindVPNClient(ip) is NetworkHost vpn)
                {
                    return vpn.Name;
                }
            }

            foreach (var host in this) // Look at known hosts
            {
                if (ip != null ? host.HasAddress(ip: ip) : host.HasAddress(mac: mac))
                {
                    return host.Name;
                }
            }

            if (ip != null && await ip.LookupName() is string lookup) // then try to resolve unkown hosts
            {
                if (IsInLocalZone(lookup))
                {
                    return lookup.Split('.')[0]; // remove DNS suffix
                }
                else
                {
                    return lookup;
                }

                // TODO remeber resolved name?
            }

            return null;
        }

        public IEnumerator<NetworkHost> GetEnumerator()
        {
            return _hosts.Values.GetEnumerator();
        }
    }
}
