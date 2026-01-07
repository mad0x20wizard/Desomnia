namespace MadWizard.Desomnia.Configuration
{
    public class SystemMonitorConfig
    {
        public const uint MIN_VERSION = 1;
        public const uint MAX_VERSION = 1;

        public required uint    Version             { get; set; }

        public TimeSpan?        Timeout             { get; set; }

        public DelayedAction?   OnIdle              { get; set; }
        public NamedAction?     OnDemand            { get; set; }
        public NamedAction?     OnSuspend           { get; set; }
        public DelayedAction?   OnSuspendTimeout    { get; set; }
        public DelayedAction?   OnResume            { get; set; }

        // public IList<ActionGroupConfig> ActionGroup { get; private set; } = [];

    }
}
