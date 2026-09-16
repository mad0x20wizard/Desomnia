using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoConfig : Network.Configuration.ModuleConfig<NetworkMonitorConfig>
    {
        public SessionMonitorConfig? SessionMonitor { get; set; }
        public DuoSessionMonitorConfig? DuoSessionMonitor { get; set; }

        internal bool UseListener => (DuoSessionMonitor?.UseListener ?? false) || NetworkMonitor.Count == 0;
    }
}
