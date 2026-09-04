namespace MadWizard.Desomnia.Configuration
{
    public abstract record WatchMetrics
    {
        public WatchExpression Watch { get; init; }
    }
}
