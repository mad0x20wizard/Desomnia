using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Neighborhood;
namespace MadWizard.Desomnia.Network.Demand
{
    internal interface IDemandDetector
    {
        NetworkHost? Examine(INetworkCapture capture);
    }
}
