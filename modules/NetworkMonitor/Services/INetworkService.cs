using PacketDotNet;

namespace MadWizard.Desomnia.Network.Services
{
    public interface INetworkService
    {
        void Startup() => Resume();

        void Resume() { }

        void ProcessPacket(EthernetPacket packet) { }

        void Suspend() { }

        void Shutdown() => Suspend();
    }
}
