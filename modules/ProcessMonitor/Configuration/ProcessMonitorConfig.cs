using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Processes.Configuration
{
    public class ProcessManagerConfig
    {
        public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    }

    public record ProcessMonitorConfig : ProcessWatchMetrics
    {
        public DelayedActionInfo? OnIdle { get; set; }
        public DelayedActionInfo? OnUsage { get; set; }

        public IList<ProcessWatchInfo> Process { get; set; } = [];
    }
}
