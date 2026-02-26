using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoStreamMonitorConfig
    {
        public required string ServiceName { get; set; } = "DuoService";

        public TimeSpan Refresh { get; set; } = TimeSpan.FromSeconds(5);

        public NamedAction? OnInstanceDemand { get; set; }

        public DelayedAction? OnInstanceIdle { get; set; }

        public DelayedAction? OnInstanceLogin{ get; set; }
        public DelayedAction? OnInstanceStarted { get; set; }
        public DelayedAction? OnInstanceStopped { get; set; }
        public DelayedAction? OnInstanceLogoff { get; set; }

        public IList<DuoInstanceInfo> Instance { get; private set; } = [];

        public bool UseFallback { get; set; } = false;

        internal DuoInstanceInfo? this[string name] => Instance.FirstOrDefault(i => i.Name == name);
    }
}
