using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class LocalHostInfo
    {
        public TrafficThreshold? MinTraffic { get; set; }

        // Options
        #region     DemandOptions
        TimeSpan?   DemandTimeout { get; set; }
        int?        DemandParallel { get; set; }

        public DemandOptions MakeDemandOptions(NetworkMonitorConfig network) => new()
        {
            Timeout = DemandTimeout ?? network.DemandTimeout,
            Parallel = DemandParallel ?? network.DemandParallel,
            Forward = false,

            Advertise = AddressAdvertisment.Never
        };
        #endregion

        // Ports
        public IList<ServiceInfo> Service { get; set; } = [];
        public IList<HTTPServiceInfo> HTTPService { get; set; } = [];
        public IEnumerable<ServiceInfo> Services => Service.Concat(HTTPService);

        // Virtual-Hosts
        public IList<LocalVirtualHostInfo> VirtualHost { get; set; } = [];

        // Filter-Rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; set; } = [];
    }
}
