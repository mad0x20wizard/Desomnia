using System.Diagnostics;

namespace MadWizard.Desomnia.Process.Manager
{
    public interface IProcessManager : IIEnumerable<IProcess>
    {
        public event EventHandler<IProcess> ProcessStarted;
        public event EventHandler<IProcess> ProcessStopped;

        public IProcess LaunchProcess(ProcessStartInfo info);
    }
}
