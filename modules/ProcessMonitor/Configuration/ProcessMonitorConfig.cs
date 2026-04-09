namespace MadWizard.Desomnia.Process.Configuration
{
    public class ProcessManagerConfig
    {
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

        public TimeSpan? PollInterval { get; init; }
    }

    public class ProcessMonitorConfig : ProcessManagerConfig
    {
        public IList<ProcessWatchInfo> Process { get; set; } = [];
    }
}
