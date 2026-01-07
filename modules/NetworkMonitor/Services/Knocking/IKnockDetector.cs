using MadWizard.Desomnia.Network.Knocking.Secrets;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Knocking
{
    public interface IKnockDetector
    {
        IEnumerable<KnockEvent> Examine(IPPacket packet, SharedSecret secret);
    }
}
