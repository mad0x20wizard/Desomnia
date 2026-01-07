using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking
{
    public interface IKnockFilter
    {
        bool ShouldFilter(IPPacket packet, KnockEvent knock);
    }

    internal class CompositeKnockFilter(IEnumerable<IKnockFilter> filters) : IKnockFilter
    {
        bool IKnockFilter.ShouldFilter(IPPacket packet, KnockEvent knock)
        {
            foreach (IKnockFilter filter in filters)
            {
                if (filter.ShouldFilter(packet, knock))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
