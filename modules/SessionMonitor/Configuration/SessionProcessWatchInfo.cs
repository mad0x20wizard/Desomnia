using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Process.Configuration;

namespace MadWizard.Desomnia.Session.Configuration
{
    public class SessionProcessWatchInfo : ProcessWatchInfo
    {
        public DelayedAction? OnSessionIdle { get; set; }
        public DelayedAction? OnSessionDemand { get; set; }
        public DelayedAction? OnSessionConsoleConnect { get; set; }
        public DelayedAction? OnSessionRemoteConnect { get; set; }
        public DelayedAction? OnSessionDisconnect { get; set; }
    }
}
