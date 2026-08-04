using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Bridge.Configuration
{
    public class BridgedSessionManagerConfig : SessionMonitorConfig<BridgedSessionManagerConfig, BridgedSessionDescriptor>
    {
        public bool? SpawnMinions { get; set; } = true;
    }

    public class BridgedSessionDescriptor : SessionWatchDescriptor
    {
        public SessionSelector? AllowControlSession { get; set; }
        public bool? AllowControlSleep { get; set; }
    }
}
