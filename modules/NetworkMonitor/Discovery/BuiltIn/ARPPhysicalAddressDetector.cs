using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network.Discovery.BuiltIn 
{
    internal class ARPPhysicalAddressDetector(DiscoveryOptions options) : IPhysicalAddressDiscovery
    {
        async Task IPhysicalAddressDiscovery.DiscoverAddress(NetworkHost host)
        {

        }
    }
}
