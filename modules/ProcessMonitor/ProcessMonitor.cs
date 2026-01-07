using Autofac;
using MadWizard.Desomnia.Process.Configuration;
using MadWizard.Desomnia.Process.Manager;
using Microsoft.Extensions.Logging;


namespace MadWizard.Desomnia.Process
{
    public class ProcessMonitor(ProcessMonitorConfig config, IProcessManager manager) : ResourceMonitor<ProcessGroup>, IStartable
    {
        public required ILogger<ProcessMonitor> Logger { get; set; }

        void IStartable.Start()
        {
            foreach (var info in config.Process)
            {
                StartTracking(new SystemProcessGroup(manager, info));
            }

            Logger.LogDebug("Startup complete");
        }

        private class SystemProcessGroup(IProcessManager manager, ProcessGroupInfo info) : ProcessGroup(info)
        {
            protected override IEnumerable<IProcess> EnumerateProcesses() => manager;
        }
    }
}
