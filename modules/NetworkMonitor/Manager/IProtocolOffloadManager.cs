namespace MadWizard.Desomnia.Network.Manager
{
    public interface IProtocolOffloadManager
    {
        public OffloadProtocol SupportedProtocols   { get; }
        public OffloadProtocol OffloadProtocols     { get; set; }
    }

    [Flags]
    public enum OffloadProtocol
    {
        None = 0,

        IPv4 = 1 << 0, // ARP offload
        IPv6 = 1 << 1, // NS offload

        IP = IPv4 | IPv6
    }
}
