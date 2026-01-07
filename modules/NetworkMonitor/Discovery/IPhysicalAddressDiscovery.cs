using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Discovery
{
    internal interface IPhysicalAddressDiscovery
    {
        Task DiscoverAddress(NetworkHost host);
    }

    internal class CompositePhysicalAddressDiscovery(IEnumerable<IPhysicalAddressDiscovery> discoverers) : IPhysicalAddressDiscovery
    {
        public async Task DiscoverAddress(NetworkHost host)
        {
            foreach (var discoverer in discoverers)
            {
                await discoverer.DiscoverAddress(host);
            }
        }
    }
}
