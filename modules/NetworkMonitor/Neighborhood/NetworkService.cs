using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Filter;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public abstract class NetworkService(string name)
    {
        public string Name { get; init; } = name;

        public abstract bool Accepts(INetworkCapture capture, FlowDirection direction = FlowDirection.Inbound);
    }
}
