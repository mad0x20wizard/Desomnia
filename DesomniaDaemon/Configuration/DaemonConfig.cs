using MadWizard.Desomnia.PowerRequest.Configuration;
using MadWizard.Desomnia.Processes.Configuration;

namespace MadWizard.Desomnia.Daemon.Configuration
{
    public class DaemonConfig
    {
        public bool UseDBus { get; set; } = true;

        public PowerManagerConfig   PowerManager    { get; set; } = new();
        public ProcessManagerConfig ProcessManager  { get; set; } = new();

    }
}
