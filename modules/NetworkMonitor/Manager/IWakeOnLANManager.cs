namespace MadWizard.Desomnia.Network.Manager
{
    public interface IWakeOnLANManager
    {
        public WakeOnLANMode SupportedModes { get; }
        public WakeOnLANMode Modes          { get; set; }
    }

    [Flags]
    public enum WakeOnLANMode
    {
        None            = 0,

        MagicPacket     = 1 << 0,
        Pattern         = 1 << 1,
    }
}
