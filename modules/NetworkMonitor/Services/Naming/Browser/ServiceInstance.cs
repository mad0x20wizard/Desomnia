using MadWizard.Desomnia.Network.Naming.Browser.Events;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using Makaretu.Dns;
using System.Collections.Concurrent;
using System.Net;

namespace MadWizard.Desomnia.Network.Naming
{
    /// <summary>
    /// A live, self-updating view of a discovered DNS-SD service instance. It is created as soon as its
    /// SRV record is known and enriched afterwards as address (and other) records arrive, signalling
    /// every change through its events. The <see cref="MulticastServiceBrowser"/> keeps it current for
    /// as long as something references it; once dropped it is garbage-collected and the browser forgets
    /// it. All mutating members are expected to be called under the network mutex.
    /// </summary>
    public sealed class ServiceInstance
    {
        public string Name { get; init; }
        public string ServiceName { get; init; }

        public DomainName DomainName { get; init; }
        public DomainName ServiceDomainName { get; init; }

        public IPService Port { get; init; }

        public DomainName HostDomainName { get; private init; } // "host.local"
        public string HostName => HostDomainName.Labels[0]; // "host"

        public IEnumerable<IPAddress> Addresses => _addresses.Keys;

        public IReadOnlyList<string>? Text 
        {
            get;

            internal set
            {
                field = value;

                Properties = new Dictionary<string, string>();

                foreach (var text in value ?? [])
                {
                    if (text.Split('=') is [var key, var val])
                    {
                        Properties[key] = val;
                    }
                }
            }
        } = null;

        public IDictionary<string, string> Properties { get; private set; } = new Dictionary<string, string>();

        public DateTime Expires { get; private set; }

        public TimeSpan TTL
        {
            get; set
            {
                Expires = DateTime.Now + (field = value);
            }
        }

        internal ServiceInstance(SRVRecord srv)
        {
            DomainName = srv.Name;
            Name = srv.InstanceName;

            ServiceDomainName = srv.ServiceDomainName;
            ServiceName = srv.ServiceName;

            Port = srv.IPPort;

            HostDomainName = srv.Target;

            TTL = srv.TTL;
        }

        #region IP address management
        readonly ConcurrentDictionary<IPAddress, IPAddressOptions> _addresses = [];

        public IPAddressOptions this[IPAddress ip]
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

        public event EventHandler<ServiceInstanceAddressEventArgs>? AddressAdded;
        public event EventHandler<ServiceInstanceAddressRemovedEventArgs>? AddressRemoved;
        public event EventHandler<ServiceInstanceRemovedEventArgs>? Removed;

        internal bool AddAddress(IPAddress ip, TimeSpan ttl)
        {
            if (_addresses.ContainsKey(ip))
            {
                _addresses[ip] = new(ttl);

                return false;
            }
            else
            {
                _addresses[ip] = new(ttl);

                AddressAdded?.Invoke(this, new(ip));

                return true;
            }
        }

        internal void RemoveAddress(IPAddress ip, ServiceInstanceRemovedReason reason)
        {
            if (_addresses.TryRemove(ip, out _))
            {
                AddressRemoved?.Invoke(this, new(ip, reason));
            }
        }
        #endregion

        internal void TriggerRemoved(ServiceInstanceRemovedReason reason)
        {
            foreach (IPAddress ip in _addresses.Keys.ToArray())
                RemoveAddress(ip, reason);

            Removed?.Invoke(this, new(reason));
            Removed = null;
        }

        /// <summary>
        /// Re-anchors every record's lifetime to now, as if it had just been seen. Used after a system
        /// resume, where the maintenance clock jumped past the TTLs while the process was frozen: the
        /// records are then re-confirmed on the network instead of being pruned as spuriously expired.
        /// </summary>
        internal void ResetTTL()
        {
            Expires = DateTime.Now + TTL;

            foreach (IPAddress ip in _addresses.Keys.ToArray())
                if (_addresses[ip].TTL is TimeSpan ttl)
                    _addresses[ip] = new(ttl); // resets the address's Expires = now + ttl
        }
    }
}
