using MadWizard.Desomnia.Network.Neighborhood.Events;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using Makaretu.Dns;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkHost(string name) : NetworkResource
    {
        public required NetworkSegment Network { get; init; }

        public virtual string Name { get; init; } = name;
        public virtual string HostName { get => field ?? Name; set; } = null!;

        /// <summary>The mDNS name of this host, e.g. "desktop.local".</summary>
        public DomainName LocalDomainName => new(HostName, "local");

        public virtual PhysicalAddress? PhysicalAddress { get; set { field = value;  PhysicalAddressChanged?.Invoke(this, new(value!)); } }

        #region Change management
        protected override void SubscribeToSelfChanges()
        {
            // delegate Address/Service events to Changed
            AddressAdded    += (_, args) => TriggerChanged(args);
            AddressRemoved  += (_, args) => TriggerChanged(args);
            ServiceAdded    += (_, args) => TriggerChanged(args);
            ServiceRemoved  += (_, args) => TriggerChanged(args);
        }
        #endregion

        #region IP addresses
        readonly ConcurrentDictionary<IPAddress, IPAddressOptions> _addresses = [];

        public virtual IPAddressOptions this[IPAddress ip]
        {
            get
            {
                if (_addresses.TryGetValue(ip, out var options))
                {
                    return options;
                }

                throw new KeyNotFoundException("IP = " + ip.ToString());
            }
        }

        public virtual IEnumerable<IPAddress> IPAddresses => _addresses.Keys;
        public IEnumerable<IPAddress> IPv4Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        public IEnumerable<IPAddress> IPv6Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);

        public IEnumerable<IPAddress> SelectIPAddressesBy(IPAddressSelectionOptions options = default) => options.Select(IPAddresses);

        public event EventHandler<AddressAddedEventArgs>? AddressAdded;
        public event EventHandler<AddressRemovedEventArgs>? AddressRemoved;
        public event EventHandler<PhysicalAddressEventArgs>? PhysicalAddressChanged;

        public virtual bool AddAddress(IPAddress ip, IPAddressOptions options = default)
        {
            ip.RemoveScopeId();

            if (_addresses.TryGetValue(ip, out IPAddressOptions existing))
            {
                if (!existing.HasFlags(IPAddressFlags.Static))
                {
                    if (existing.Expires < options.Expires)
                    {
                        existing.Expires = options.Expires;

                        _addresses[ip] = existing;

                        TriggerChanged();
                    }
                }

                return false;
            }
            else
            {
                _addresses[ip] = options;

                AddressAdded?.Invoke(this, new(ip, options.Expires));

                return true;
            }
        }
        public bool ShouldAddressExpire(IPAddress ip, out DateTime expires)
        {
            expires = DateTime.MaxValue;

            if (this[ip].Expires is DateTime date)
            {
                expires = date;

                return true;
            }

            return false;
        }
        public bool HasAddress(PhysicalAddress? mac)
        {
            return mac != null && mac.Equals(this.PhysicalAddress);
        }
        public bool HasAddress(IPAddress? ip)
        {
            return ip != null && this.IPAddresses.Contains(ip);
        }
        public bool HasAddress(PhysicalAddress? mac = null, IPAddress? ip = null, bool both = false)
        {
            if (mac != null || ip != null)
            {
                bool hasMAC = HasAddress(mac);
                bool hasIP  = HasAddress(ip);

                return both ? hasMAC && hasIP : hasMAC || hasIP;
            }

            return false;
        }
        public virtual bool RemoveAddress(IPAddress ip, bool expired = false)
        {
            if (_addresses.Remove(ip, out _))
            {
                AddressRemoved?.Invoke(this, new(ip, expired));

                return true;
            }

            return false;
        }
        #endregion

        #region Services
        readonly ConcurrentDictionary<NetworkService, ServiceOptions> _services = [];

        public NetworkService? this[IPService service] => Services.FirstOrDefault(net => net.Contains(service));

        public virtual ServiceOptions this[NetworkService service]
        {
            get
            {
                if (_services.TryGetValue(service, out var options))
                {
                    return options;
                }

                throw new KeyNotFoundException("NetworkService = " + service.ToString());
            }
        }

        public virtual IEnumerable<NetworkService> Services => _services.Keys;

        public event EventHandler<ServiceAddedEventArgs>? ServiceAdded;
        public event EventHandler<ServiceRemovedEventArgs>? ServiceRemoved;

        public virtual bool AddService(NetworkService service, ServiceOptions options = default)
        {
            if (_services.TryGetValue(service, out ServiceOptions existingOpts))
            {
                // Already known: a re-advertisement only pushes the expiry further out, mirroring
                // how IP addresses are refreshed. Statically configured services never expire.
                if (!existingOpts.HasFlags(ServiceFlags.Static))
                {
                    if (existingOpts.Expires < options.Expires)
                    {
                        existingOpts.Expires = options.Expires;

                        _services[service] = existingOpts;

                        TriggerChanged();
                    }
                }

                return false;
            }

            foreach (var existingSrv in _services.Keys)
                if (existingSrv.Intersect(service).Any())
                    throw new ArgumentException($"NetworkService {existingSrv} conflicts with {service}.");

            _services[service] = options;

            ServiceAdded?.Invoke(this, new(service, options.Expires));

            return true;
        }
        public bool ShouldServiceExpire(NetworkService service, out DateTime expires)
        {
            expires = DateTime.MaxValue;

            if (this[service].Expires is DateTime date)
            {
                expires = date;

                return true;
            }

            return false;
        }
        public virtual bool RemoveService(NetworkService service, bool expired = false)
        {
            if (_services.Remove(service, out _))
            {
                ServiceRemoved?.Invoke(this, new(service, expired));

                return true;
            }

            return false;
        }
        #endregion
    }
}
