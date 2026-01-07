using Microsoft.Extensions.Caching.Memory;
using System.Net;

namespace MadWizard.Desomnia.Network.Reachability
{
    public class ReachabilityCache
    {
        static readonly TimeSpan IP_CACHE_MAX_TTL = TimeSpan.FromSeconds(30);

        readonly MemoryCache _cache = new(new MemoryCacheOptions());

        internal void Write(IPAddress ip, IPPort? port, bool reachable)
        {
            var entry = new ReachabilityCacheEntry(reachable);

            _cache.Set(ip, entry, IP_CACHE_MAX_TTL);

            if (port is IPPort p)
            {
                if (p.Protocol == IPProtocol.TCP)
                    _cache.Set(new TCPEndPoint(ip, p.Port), entry, IP_CACHE_MAX_TTL);
                else if (p.Protocol == IPProtocol.UDP)
                    _cache.Set(new UDPEndPoint(ip, p.Port), entry, IP_CACHE_MAX_TTL);
                _cache.Set(new IPEndPoint(ip, p.Port), entry, IP_CACHE_MAX_TTL);
            }
        }

        public IDictionary<IPAddress, bool?> Read(ReachabilityTest test)
        {
            var results = new Dictionary<IPAddress, bool?>();

            foreach (var ip in test)
            {
                results[ip] = Read(ip, test.Timeout);
            }

            return results;
        }
        
        public bool? Read(IPAddress ip, TimeSpan timeout) => Read(ip as object, timeout);
        public bool? Read(IPEndPoint endpoint, TimeSpan timeout) => Read(endpoint as object, timeout);

        private bool? Read(object key, TimeSpan timeout)
        {
            if (_cache.TryGetValue(key, out ReachabilityCacheEntry entry) && !entry.IsExpired(timeout))
            {
                return entry.Alive;
            }

            return null;
        }
    }
}
