namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct WatchOptions
    {
        public WatchMode    Mode            { get; init; }

        public ushort[]     UDPPorts        { get; init; }

        public bool         Yield           { get; init; }
    }

    public enum WatchMode
    {
        None = 0,

        Normal,
        Promiscuous
    }
}
