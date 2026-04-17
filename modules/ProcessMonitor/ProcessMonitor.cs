using Autofac;
using MadWizard.Desomnia.Process.Configuration;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Process
{
    public class ProcessMonitor(ProcessMonitorConfig config) : ResourceMonitor<ProcessWatch>, IStartable
    {
        public required ILogger<ProcessMonitor> Logger { get; set; }

        public required Func<ProcessWatchInfo, ProcessWatch> CreateWatch { private get; init; }

        void IStartable.Start()
        {
            foreach (var info in config.Process)
            {
                StartTracking(CreateWatch(info));
            }

            Logger.LogDebug("Startup complete");
        }
    }
}