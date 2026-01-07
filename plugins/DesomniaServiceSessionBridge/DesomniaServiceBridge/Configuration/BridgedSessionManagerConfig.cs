using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Bridge.Configuration
{
    public class BridgedSessionManagerConfig : SessionConfig<BridgedSessionDescriptor>
    {
        public bool? SpawnMinions { get; set; } = true;
    }

    public class BridgedSessionDescriptor : SessionDescriptor
    {
        public SessionMatcher? AllowControlSession { get; set; }
        public bool? AllowControlSleep { get; set; }
    }
}
