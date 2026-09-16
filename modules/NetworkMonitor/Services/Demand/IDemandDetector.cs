using MadWizard.Desomnia.Network.Neighborhood;
namespace MadWizard.Desomnia.Network.Demand
{
    internal interface IDemandDetector
    {
        NetworkHost? Examine(in CaptureSummary packet);
    }
}
