using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Demand.Filter
{
    public interface IDemandPacketFilter : IPacketFilter
    {

    }

    internal class CompositeDemandPacketFilter(IEnumerable<IDemandPacketFilter> filters) : IDemandPacketFilter
    {
        bool IPacketFilter.ShouldFilter(EthernetPacket packet)
        {
            foreach (IDemandPacketFilter filter in filters)
            {
                if (filter.ShouldFilter(packet))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
