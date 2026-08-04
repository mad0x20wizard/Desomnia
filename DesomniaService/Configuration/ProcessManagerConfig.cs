namespace MadWizard.Desomnia.Service.Configuration
{
    internal class ProcessManagerConfig
    {
        public ProcessTrafficWatch WatchTraffic { get; set; } = default;
    }

    internal enum ProcessTrafficWatch
    {
        Passive = 0, // default

        Active
    }
}
