using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    internal class GenericServiceFilterRule(NetworkService service) : IPFilterRule
    {
        override public bool Matches(EthernetPacket packet)
        {
            if (service.Accepts(packet))
            {
                return base.Matches(packet);
            }

            return false;
        }

    }
}
