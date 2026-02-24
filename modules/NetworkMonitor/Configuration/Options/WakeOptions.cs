namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct WakeOptions
    {
        public WakeType     Type    { get; init; }
        public ushort       Port    { get; init; }

        public TimeSpan     Timeout { get; init; }
        public TimeSpan?    Repeat  { get; init; }

        public bool         Silent  { get; init; }
    }

    [Flags]
    public enum WakeType
    {
        None = 0,

        Link = 1,
        Network = 2,

        Unicast = 4,
        Broadcast = 8,

        Auto = 128,
    }
}
