using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoSessionMonitorConfig
    {
        public required string ServiceName              { get; set; } = "DuoService";

        public TimeSpan PollInterval                    { get; set; } = TimeSpan.FromSeconds(1); // deprecated

        public bool UseListener                         { get; set; } = false;
        public bool UsePolling                          { get; set; } = false;

        public ActionInfo? OnDemand                     { get; set; }
        public DelayedActionInfo? OnIdle                { get; set; }

        public ActionInfo? OnInstanceDemand             { get; set; }
        public DelayedActionInfo? OnInstanceIdle        { get; set; }
        public ScheduledActionInfo? OnInstanceLogin     { get; set; }
        public ScheduledActionInfo? OnInstanceStarted   { get; set; }
        public ScheduledActionInfo? OnInstanceStopped   { get; set; }
        public ScheduledActionInfo? OnInstanceLogout    { get; set; }

        public bool WatchStreamTraffic { get; set; } = true;

        public IList<DuoInstanceWatchInfo> Instance { get; private set; } = [];

        internal DuoInstanceWatchInfo? this[string name] => Instance.FirstOrDefault(i => i.Name == name);
    }
}
