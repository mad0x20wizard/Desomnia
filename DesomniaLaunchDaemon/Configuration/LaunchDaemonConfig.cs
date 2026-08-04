using MadWizard.Desomnia.Processes.Configuration;

namespace MadWizard.Desomnia.LaunchDaemon.Configuration
{
    public class LaunchDaemonConfig
    {
        /// <summary>
        /// The persistent ProcessManager settings (<c>&lt;?global ProcessManager:pollInterval="..."?&gt;</c>),
        /// read for the <c>pollInterval</c> alone — the platform manager has to be built with it.
        /// </summary>
        public ProcessManagerConfig ProcessManager { get; set; } = new();
    }
}
