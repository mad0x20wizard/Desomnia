using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoInstanceInfo : SessionWatchInfo
    {
        public required string Name { get; set; }

        public ActionInfo? OnDemand { get; set; }

        public DelayedActionInfo? OnStart { get; set; }
        public DelayedActionInfo? OnStop { get; set; }

        public bool? PreventIdleIfStreaming { get; set; }

        public TransmissionThreshold? MinStreamTraffic { get; set; }

        public DuoInstanceInfo()
        {
            ClockRemote = true; // Duo sessions should be generally checked for idleness
        }
    }
}
