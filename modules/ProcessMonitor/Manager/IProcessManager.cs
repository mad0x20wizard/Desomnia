using System.Diagnostics;

namespace MadWizard.Desomnia.Process.Manager
{
    public interface IProcessManager : IIEnumerable<IProcess>
    {
        IProcess this[int pid] { get; }

        IProcess LaunchProcess(ProcessStartInfo info);

        event EventHandler<IProcess> ProcessStarted;
        event EventHandler<IProcess> ProcessStopped;
    }
}
