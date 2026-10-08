namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct WatchOptions
    {
        public WatchMode    Mode            { get; init; }
        public ushort[]     UDPPorts        { get; init; }
    }

    public enum WatchMode
    {
        Normal = 0,
        Promiscuous
    }
}
