using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoStreamMonitorConfig
    {
        public required string ServiceName { get; set; } = "DuoService";

        public DelayedAction? OnIdle                { get; set; }
        public DelayedAction? OnDemand              { get; set; }

        public DelayedAction? OnInstanceDemand      { get; set; }

        public DelayedAction? OnInstanceIdle        { get; set; }

        public DelayedAction? OnInstanceLogin       { get; set; }
        public DelayedAction? OnInstanceStarted     { get; set; }
        public DelayedAction? OnInstanceStopped     { get; set; }
        public DelayedAction? OnInstanceLogout      { get; set; }

        public IList<DuoInstanceInfo> Instance      { get; private set; } = [];

        public bool UseFallback                     { get; set; } = false;
        public bool UsePolling                      { get; set; } = false;

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

        internal DuoInstanceInfo? this[string name] => Instance.FirstOrDefault(i => i.Name == name);
    }
}
