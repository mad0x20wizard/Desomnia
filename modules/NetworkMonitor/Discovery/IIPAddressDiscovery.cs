using MadWizard.Desomnia.Network.Neighborhood;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Discovery
{
    public interface IIPAddressDiscovery
    {
        Task DiscoverIPAddresses(NetworkHost host, AddressFamily family);
    }

    internal class CompositeIPAddressDiscovery(IEnumerable<IIPAddressDiscovery> discoverers) : IIPAddressDiscovery
    {
        public async Task DiscoverIPAddresses(NetworkHost host, AddressFamily family)
        {
            foreach (var discoverer in discoverers)
            {
                await discoverer.DiscoverIPAddresses(host, family);
            }
        }
    }
}
