using ConcurrentCollections;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkHostRange : IIEnumerable<IPAddressRange>, IDisposable
    {
        readonly ConcurrentHashSet<NetworkHost> _hosts = [];
        readonly ConcurrentHashSet<IPAddressRange> _ranges = [];

        public event EventHandler<IPAddressRange>? AddressRangeAdded;
        public event EventHandler<IPAddressRange>? AddressRangeRemoved;

        #region NetworkHost address synchronization
        public bool AddHostAddressRange(NetworkHost host)
        {
            if (_hosts.Add(host))
            {
                host.AddressAdded += Host_AddressAdded;
                host.AddressRemoved += Host_AddressRemoved;

                foreach (var ip in host.IPAddresses)
                {
                    AddAddress(ip);
                }

                return true;
            }

            return false;
        }

        private void Host_AddressAdded(object? sender, AddressAddedEventArgs args)
        {
            AddAddress(args.IPAddress);
        }

        private void Host_AddressRemoved(object? sender, AddressRemovedEventArgs args)
        {
            RemoveAddress(args.IPAddress);
        }

        public bool RemoveHostAddressRange(NetworkHost host)
        {
            if (_hosts.TryRemove(host))
            {
                host.AddressRemoved -= Host_AddressRemoved;
                host.AddressAdded -= Host_AddressAdded;

                foreach (var ip in host.IPAddresses)
                {
                    RemoveAddress(ip);
                }

                return true;
            }

            return false;
        }
        #endregion

        public bool AddAddress(IPAddress ip) => AddAddressRange(new(ip));

        public bool AddAddressRange(IPAddressRange range)
        {
            if (_ranges.Add(range))
            {
                AddressRangeAdded?.Invoke(this, range);

                return true;
            }

            return false;
        }

        public virtual bool Contains(IPAddress ip)
        {
            return _ranges.Any(range => range.Contains(ip));
        }

        public virtual bool RemoveAddress(IPAddress ip)
        {
            var ranges = _ranges.Where(range =>
                range.AddressCount == 1 &&
                range.Begin.Equals(ip) &&
                range.End.Equals(ip))
                .ToArray();

            bool removed = false;
            foreach (var range in ranges)
            {
                removed |= RemoveAddressRange(range);
            }

            return removed;
        }

        public bool RemoveAddressRange(IPAddressRange range)
        {
            if (_ranges.TryRemove(range))
            {
                AddressRangeRemoved?.Invoke(this, range);

                return true;
            }

            return false;
        }

        public virtual IEnumerator<IPAddressRange> GetEnumerator() => _ranges.GetEnumerator();

        void IDisposable.Dispose()
        {
            lock (_hosts)
            {
                foreach (var host in _hosts.ToArray())
                {
                    RemoveHostAddressRange(host);
                }
            }
        }
    }
}
