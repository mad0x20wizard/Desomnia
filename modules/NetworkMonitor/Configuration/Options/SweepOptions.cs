namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct SweepOptions
    {
        public TimeSpan Frequency { get; init; }
        public TimeSpan Delay { get; init; }
    }
}
