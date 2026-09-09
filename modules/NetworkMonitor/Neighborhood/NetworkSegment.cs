using Autofac.Features.Indexed;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkSegment : IIEnumerable<NetworkHost>
    {
        /// <summary>The pseudo-TLD that multicast DNS is authoritative for (RFC 6762 §3).</summary>
        private static readonly string LocalZone = ".local";

        public required ILogger<NetworkSegment> Logger { protected get; init; }

        public AsyncLock Mutex { get; } = new();

        public required NetworkDevice Device { private get; init; }

        public NetworkRouter? DefaultGateway => this.OfType<NetworkRouter>().FirstOrDefault(r => r.IsDefaultGateway(Device.Interface));

        public required LocalNetworkRange LocalRange { get; init; }
        public required IIndex<string, NetworkHostRange> Ranges { get; init; }

        public event EventHandler<NetworkHostEventArgs>? HostAdded;
        public event EventHandler<NetworkHostEventArgs>? HostRemoved;

        readonly ConcurrentDictionary<string, NetworkHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

        readonly MemoryCache _cacheHostName = new(new MemoryCacheOptions());

        readonly IIndex<PhysicalAddress, NetworkHost> _indexHostByMAC;
        readonly IIndex<IPAddress, NetworkHost> _indexHostByIP;

        public NetworkSegment()
        {
            var index = new NetworkHostIndex(this);

            _indexHostByMAC = index;
            _indexHostByIP = index;
        }

        public NetworkHost? this[string name, bool byHostName = false]
        {
            get
            {
                if (byHostName)
                {
                    return this.FirstOrDefault(host => string.Equals(host.HostName, name, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    return _hosts.TryGetValue(name, out var host) ? host : null;
                }
            }
        }

        public NetworkHost? this[IPAddress? ip]
        {
            get => ip is not null && _indexHostByIP.TryGetValue(ip, out var host) ? host : null;
        }

        public NetworkHost? this[PhysicalAddress? mac]
        {
            get => mac is not null && _indexHostByMAC.TryGetValue(mac, out var host) ? host : null;
        }

        public void AddHost(NetworkHost host)
        {
            if (_hosts.TryAdd(host.Name, host))
            {
                HostAdded?.Invoke(this, new(host));
            }
            else
                throw new ArgumentException($"Host '{host.Name}' already exists on network '{Device.Interface.Name}'.");
        }

        public void RememberHostName(object key, string name, TimeSpan duration)
        {
            _cacheHostName.Set(key, name, duration);
        }

        public bool RemoveHost(NetworkHost host)
        {
            if (_hosts.TryRemove(host.Name, out var removed))
            {
                HostRemoved?.Invoke(this, new(removed));

                return true;
            }

            return false;
        }

        public bool IsInLocalZone(string domainName)
        {
            const StringComparison comparision = StringComparison.OrdinalIgnoreCase;

            if (domainName.EndsWith(LocalZone, comparision))
                return true;
            if (Device.Interface.DNSSuffix is string suffix && domainName.EndsWith(suffix, comparision))
                return true;

            return false;
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

    file class NetworkHostIndex : IIndex<IPAddress, NetworkHost>, IIndex<PhysicalAddress, NetworkHost>
    {
        readonly ConcurrentDictionary<IPAddress, NetworkHost> _hostsByAddress = [];
        readonly ConcurrentDictionary<PhysicalAddress, NetworkHost> _hostsByPhysicalAddress = [];

        internal NetworkHostIndex(NetworkSegment network)
        {
            network.HostAdded += IndexHost;
            network.HostRemoved += UnindexHost;
        }

        private void IndexHost(object? sender, NetworkHostEventArgs args)
        {
            args.Host.AddressAdded += Host_AddressAdded;
            args.Host.AddressRemoved += Host_AddressRemoved;
            args.Host.PhysicalAddressChanged += Host_PhysicalAddressChanged;

            foreach (var ip in args.Host.IPAddresses)
                _hostsByAddress[ip] = args.Host;

            if (args.Host.PhysicalAddress is PhysicalAddress mac && !mac.Equals(PhysicalAddress.None))
                _hostsByPhysicalAddress[mac] = args.Host;
        }

        private void UnindexHost(object? sender, NetworkHostEventArgs args)
        {
            args.Host.AddressAdded -= Host_AddressAdded;
            args.Host.AddressRemoved -= Host_AddressRemoved;
            args.Host.PhysicalAddressChanged -= Host_PhysicalAddressChanged;

            foreach (var pair in _hostsByAddress)
                if (ReferenceEquals(pair.Value, args.Host))
                    _hostsByAddress.TryRemove(pair.Key, out _);

            foreach (var pair in _hostsByPhysicalAddress)
                if (ReferenceEquals(pair.Value, args.Host))
                    _hostsByPhysicalAddress.TryRemove(pair.Key, out _);
        }

        private void Host_AddressAdded(object? sender, AddressEventArgs args)
        {
            if (sender is NetworkHost host)
                _hostsByAddress[args.IPAddress] = host;
        }

        private void Host_AddressRemoved(object? sender, AddressRemovedEventArgs args)
        {
            if (_hostsByAddress.TryGetValue(args.IPAddress, out var host) && ReferenceEquals(host, sender))
                _hostsByAddress.TryRemove(args.IPAddress, out _);
        }

        private void Host_PhysicalAddressChanged(object? sender, PhysicalAddressEventArgs args)
        {
            if (sender is not NetworkHost host)
                return;

            // The event deliberately reports only the new address. Physical-address changes are
            // rare, so removing the host's former entry here keeps packet lookups allocation-free.
            foreach (var pair in _hostsByPhysicalAddress)
                if (ReferenceEquals(pair.Value, host))
                    _hostsByPhysicalAddress.TryRemove(pair.Key, out _);

            if (args.PhysicalAddress is PhysicalAddress mac && !mac.Equals(PhysicalAddress.None))
                _hostsByPhysicalAddress[mac] = host;
        }

        NetworkHost IIndex<IPAddress, NetworkHost>.this[IPAddress ip] => _hostsByAddress[ip];

        NetworkHost IIndex<PhysicalAddress, NetworkHost>.this[PhysicalAddress mac] => _hostsByPhysicalAddress[mac];

        bool IIndex<IPAddress, NetworkHost>.TryGetValue(IPAddress ip, [MaybeNullWhen(false)] out NetworkHost host)
        {
            return _hostsByAddress.TryGetValue(ip, out host);
        }

        bool IIndex<PhysicalAddress, NetworkHost>.TryGetValue(PhysicalAddress mac, [MaybeNullWhen(false)] out NetworkHost host)
        {
            return _hostsByPhysicalAddress.TryGetValue(mac, out host);
        }
    }
}
