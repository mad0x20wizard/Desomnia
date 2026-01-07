using PacketDotNet;

namespace MadWizard.Desomnia.Network.Filter
{
    public interface IPacketFilter
    {
        bool ShouldFilter(EthernetPacket packet);
    }

    internal class CompositePacketFilter(IEnumerable<IPacketFilter> filters) : IPacketFilter
    {
        bool IPacketFilter.ShouldFilter(EthernetPacket packet)
        {
            foreach (IPacketFilter filter in filters)
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
