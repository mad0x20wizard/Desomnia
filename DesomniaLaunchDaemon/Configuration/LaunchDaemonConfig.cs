namespace MadWizard.Desomnia.LaunchDaemon.Configuration
{
    public class LaunchDaemonConfig
    {
        /// <summary>
        /// The persistent ProcessManager settings supplied through <c>&lt;?system?&gt;</c> directives.
        /// They are boot-time choices because the platform manager and its GPU decorator are
        /// selected before the persistent container is built.
        /// </summary>
        public ProcessManagerConfig ProcessManager { get; set; } = new();
    }
}
