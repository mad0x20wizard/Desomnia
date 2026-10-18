using PacketDotNet;
using SharpPcap;

namespace MadWizard.Desomnia.Network.Device.Filter
{
    public interface IDevicePacketFilter
    {
        bool FilterIncoming(PacketCapture packet);

        bool FilterOutgoing(Packet packet);
    }
}
