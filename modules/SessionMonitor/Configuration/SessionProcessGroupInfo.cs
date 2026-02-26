using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Process.Configuration;

namespace MadWizard.Desomnia.Session.Configuration
{
    public class SessionProcessGroupInfo : ProcessGroupInfo
    {
        public DelayedAction? OnSessionIdle { get; set; }
        public DelayedAction? OnSessionDemand { get; set; }
    }
}
