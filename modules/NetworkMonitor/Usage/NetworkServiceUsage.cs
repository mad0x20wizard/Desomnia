using MadWizard.Desomnia.Network.Neighborhood;

namespace MadWizard.Desomnia.Network
{
    public class NetworkServiceUsage(NetworkService service, long bytes) : NetworkUsage(bytes)
    {
        public NetworkService Service => service;

        public override string ToString() => service.Name;
    }
}
