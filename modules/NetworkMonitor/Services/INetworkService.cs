using PacketDotNet;

namespace MadWizard.Desomnia.Network
{
    public interface INetworkService
    {
        Task Startup() => Resume();

        async Task AfterStartup() { }

        Task Resume() => Task.CompletedTask;

        void ProcessPacket(EthernetPacket packet) { }

        async Task BeforeSuspend() { }

        Task Suspend() => Task.CompletedTask;

        Task Shutdown(NetworkShutdownReason reason) => Suspend();
    }

    public enum NetworkShutdownReason
    {
        ApplicationShutdown = 0,

        /// <summary>
        /// The interface is still operational (unlike <see cref="InterfaceDisconnected"/>),
        /// but about to be shut down.
        ///
        /// Reserved for a coordinated shutdown while the interface is still usable.
        /// An externally disabled or disconnected interface uses InterfaceDisconnected.
        /// </summary>
        InterfaceShutdown = 1,

        InterfaceDisconnected = 2,
    }
}
