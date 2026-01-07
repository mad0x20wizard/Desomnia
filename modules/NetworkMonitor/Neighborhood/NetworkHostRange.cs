using ConcurrentCollections;
using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class NetworkHostRange
    {
        readonly ConcurrentHashSet<IPAddressRange> _ranges = [];

        public event EventHandler<IPAddressRange>? AddressRangeAdded;
        public event EventHandler<IPAddressRange>? AddressRangeRemoved;

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

        public bool RemoveAddressRange(IPAddressRange range)
        {
            if (_ranges.TryRemove(range))
            {
                AddressRangeRemoved?.Invoke(this, range);

                return true;
            }

            return false;
        }
    }
}
