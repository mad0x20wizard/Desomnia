using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Duo.Configuration
{
    public class DuoConfig : Network.Configuration.ModuleConfig<NetworkMonitorConfig>
    {
        public SessionMonitorConfig? SessionMonitor { get; set; }
        public DuoSessionMonitorConfig? DuoSessionMonitor { get; set; }

        internal bool UseListener
        {
            get
            {
                if (DuoSessionMonitor is DuoSessionMonitorConfig config)
                {
                    if (config.WatchMode.HasFlag(WatchMode.Listener))
                    {
                        if (config.WatchMode.HasFlag(WatchMode.Capture))
                            throw new FormatException("WatchMode must be either 'Listener' or 'Capture'");

                        return true;
                    }

                    if (NetworkMonitor.Count == 0)
                    {
                        if (config.WatchMode.HasFlag(WatchMode.Capture))
                            throw new FormatException("WatchMode is 'Capture' - at least one <NetworkMonitor> must be present");

                        return true;
                    }
                }

                return false;
            }
        }
    }
}
