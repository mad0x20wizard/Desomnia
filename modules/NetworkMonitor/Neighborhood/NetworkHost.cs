using MadWizard.Desomnia.Network.Neighborhood.Events;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkHost(string name)
    {
        public virtual string Name { get; init; } = name;
        public virtual string HostName { get => field ?? Name; set; } = null!;

        public required NetworkSegment Network { get; init; }

        public virtual PhysicalAddress? PhysicalAddress { get; set { field = value;  PhysicalAddressChanged?.Invoke(this, new(value!)); } }

        readonly ConcurrentDictionary<IPAddress, DateTime?> _addresses = [];

        public virtual IEnumerable<IPAddress> IPAddresses => _addresses.Keys;
        public IEnumerable<IPAddress> IPv4Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        public IEnumerable<IPAddress> IPv6Addresses => IPAddresses.Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);

        public event EventHandler<AddressAddedEventArgs>? AddressAdded;
        public event EventHandler<AddressRemovedEventArgs>? AddressRemoved;
        public event EventHandler<PhysicalAddressEventArgs>? PhysicalAddressChanged;

        public bool AddAddress(IPAddress ip, TimeSpan? lifetime = null)
        {
            ip.RemoveScopeId();

            var expires = lifetime != null ? DateTime.Now + lifetime : null;

            if (_addresses.ContainsKey(ip))
            {
                if (_addresses[ip] < expires)
                {
                    _addresses[ip] = expires;
                }

                return false;
            }
            else
            {
                _addresses[ip] = expires;

                AddressAdded?.Invoke(this, new(ip, expires));

                return true;
            }
        }

        public bool ShouldAddressExpire(IPAddress ip, out DateTime expires)
        {
            expires = DateTime.MaxValue;

            if (_addresses.ContainsKey(ip) && _addresses[ip] is DateTime date)
            {
                expires = date;

                return true;
            }

            return false;
        }

        public bool HasAddress(PhysicalAddress? mac = null, IPAddress? ip = null, bool both = false)
        {
            if (mac != null || ip != null)
            {
                bool hasMac = mac != null && mac.Equals(this.PhysicalAddress);
                bool hasIP = ip != null && this.IPAddresses.Contains(ip);

                return both ? hasMac && hasIP : hasMac || hasIP;
            }

            return false;
        }

        public bool RemoveAddress(IPAddress ip, bool expired = false)
        {
            if (_addresses.Remove(ip, out _))
            {
                AddressRemoved?.Invoke(this, new(ip, expired));

                return true;
            }

            return false;
        }
    }
}
