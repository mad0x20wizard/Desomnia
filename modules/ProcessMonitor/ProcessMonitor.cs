using Autofac;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Watch;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes
{
    public class ProcessMonitor(ProcessMonitorConfig config) : ResourceMonitor<ProcessWatch>, IStartable
    {
        public required ILogger<ProcessMonitor> Logger { get; set; }

        public required Func<ProcessWatchMetrics, AnyProcessWatch>  CreateAnyProcessWatch   { private get; init; }
        public required Func<ProcessWatchInfo, PatternProcessWatch> CreateProcessWatch      { private get; init; }

        void IStartable.Start()
        {
            Event(nameof(Idle)).AddAction(config.OnIdle);
            Event(nameof(Usage)).AddAction(config.OnUsage);

            if (config.HasThresholds)
            {
                StartTracking(CreateAnyProcessWatch(config));
            }

            foreach (var info in config.Process)
            {
                StartTracking(CreateProcessWatch(info));
            }

            Logger.LogDebug("Startup complete; {Count} procceses watched.", config.Process.Count);
        }
    }
}
