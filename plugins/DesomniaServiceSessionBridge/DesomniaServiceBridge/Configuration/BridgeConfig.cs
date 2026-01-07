namespace MadWizard.Desomnia.Service.Bridge.Configuration
{
    public class BridgeConfig
    {
        public TimeSpan? Timeout { get; set; }

        public BridgedSessionManagerConfig? SessionMonitor { get; set; }

    }
}
