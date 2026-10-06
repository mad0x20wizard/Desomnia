using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;

namespace MadWizard.Desomnia.Network.Handoff.Registration
{
    public class HandoffServiceInfo : WatchedServiceInfo
    {
        public HandoffServiceInfo()
        {
            AdvertiseTimeout = TimeSpan.Zero; // proxied services should be answered immediately
        }

        public HandoffServiceInfo(AdvertiseOptions options) : this()
        {
            AdvertiseHostTTL    = options.HostTTL;
            AdvertiseServiceTTL = options.ServiceTTL;
        }
    }
}
