namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct PingOptions
    {
        public readonly TimeSpan    Timeout { get; init; }
        public readonly TimeSpan?   Frequency { get; init; }
    }
}
