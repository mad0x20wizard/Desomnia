using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;

namespace MadWizard.Desomnia.Session.Configuration
{
    // Constructors are not inherited: the text-content constructor must be redeclared,
    // otherwise the binder cannot deliver the pattern to this derived type.
    public record SessionProcessWatchInfo(string pattern) : ProcessWatchInfo(pattern) // <- XML text content
    {
        public DelayedActionInfo? OnSessionIdle { get; set; }
        public DelayedActionInfo? OnSessionDemand { get; set; }
        public DelayedActionInfo? OnSessionConsoleConnect { get; set; }
        public DelayedActionInfo? OnSessionRemoteConnect { get; set; }
        public DelayedActionInfo? OnSessionDisconnect { get; set; }
    }
}
