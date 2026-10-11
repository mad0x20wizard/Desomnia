using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class NetworkHostInfo : IPAddressInfo
    {
        public AutoDiscoveryType? AutoDetect { get; set; }

        public required string Name { get; set; }
        public string? HostName { get; set; }

        public PhysicalAddress? MAC { get; set; }

        public bool Trace { get; set; } = false; // remove in favor of Wireshark remoting?

        public bool IsNameOnly
        {
            get
            {
                if (AutoDetect != null)
                    return false;

                if (HostName != null)
                    return false;
                if (MAC != null)
                    return false;

                if (IPAddresses.Any())
                    return false;

                if (Services.Any())
                    return false;

                if (Trace != false)
                    return false;

                return true;
            }
        }

        #region Services
        private IList<ServiceInfo> Service { get; set; } = [];
        private SleepProxyServiceInfo? SleepProxyService { get; set; }

        public virtual IEnumerable<ServiceInfo> Services 
        {
            get
            {
                foreach (var service in Service)
                    yield return service;

                if (SleepProxyService != null)
                    yield return SleepProxyService;
            }
        }
        #endregion
    }
}
