using Microsoft.Extensions.Caching.Memory;
using System.Net;

namespace MadWizard.Desomnia.Network.Reachability
{
    public class ReachabilityCache
    {
        static readonly TimeSpan IP_CACHE_MAX_TTL = TimeSpan.FromSeconds(30);

        readonly MemoryCache _cache = new(new MemoryCacheOptions());

        // Endpoints are retained as cache keys and must not be mutated after writing.
        internal void Write(IPEndPoint endpoint, bool reachable)
        {
            var entry = new ReachabilityCacheEntry(reachable);

            _cache.Set(endpoint.Address, entry, IP_CACHE_MAX_TTL);

            if (endpoint.Port != 0)
            {
                _cache.Set(endpoint, entry, IP_CACHE_MAX_TTL);
                if (endpoint is IPServiceEndPoint)
                    _cache.Set(new IPEndPoint(endpoint.Address, endpoint.Port), entry, IP_CACHE_MAX_TTL);
            }
        }

        public IDictionary<IPAddress, bool?> Read(ReachabilityTest test)
        {
            var results = new Dictionary<IPAddress, bool?>();

            foreach (var ip in test)
            {
                results[ip] = test is ServiceReachabilityTest service
                    ? Read(service.EndPoint, test.Timeout)
                    : Read(ip, test.Timeout);
            }

            return results;
        }
        
        public bool? Read(IPAddress ip, TimeSpan timeout) => Read(ip as object, timeout);
        public bool? Read(IPEndPoint endpoint, TimeSpan timeout) => endpoint.Port == 0 
            ? Read(endpoint.Address, timeout) 
            : Read(endpoint as object, timeout);

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
