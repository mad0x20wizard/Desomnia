using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes.Watch
{
    public class AnyProcessWatch() : ProcessWatch(null)
    {
        protected override bool ShouldWatchProcess(IProcess process) => true;
    }
}
