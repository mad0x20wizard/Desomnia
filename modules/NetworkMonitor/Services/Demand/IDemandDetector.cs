using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand
{
    internal interface IDemandDetector
    {
        NetworkHost? Examine(EthernetPacket packet);
    }
}
