using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes.Watch
{
    public class AnyProcessWatch(ProcessWatchMetrics metrics) : ProcessWatch(metrics)
    {
        protected override bool ShouldWatchProcess(IProcess process) => true;
    }
}
