using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Discovery
{
    internal interface IRouterDiscovery
    {
        Task DiscoverRouters(NetworkSegment network);
    }

    internal class CompositeRouterDiscovery(IEnumerable<IRouterDiscovery> discoverers) : IRouterDiscovery
    {
        public async Task DiscoverRouters(NetworkSegment network)
        {
            foreach (var discoverer in discoverers)
            {
                await discoverer.DiscoverRouters(network);
            }
        }
    }
}
