using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class WatchedHostInfo : NetworkHostInfo
    {
        public TrafficSpeed? MinTraffic { get; set; }

        // Options
        #region     DemandOptions
        TimeSpan?   DemandTimeout       { get; set; }
        bool?       DemandForward       { get; set; }
        int?        DemandParallel      { get; set; }


        AddressAdvertisment? Advertise  { get; set; }

        public virtual DemandOptions MakeDemandOptions(NetworkMonitorConfig network) => new()
        {
            Timeout     = DemandTimeout ?? network.DemandTimeout,
            Forward     = DemandForward ?? network.DemandForward,
            Parallel    = DemandParallel ?? network.DemandParallel,

            Advertise   = Advertise ?? network.Advertise
        };
        #endregion

        // Events
        public NamedAction?     OnServiceDemand { get; set; }
        public NamedAction?     OnDemand        { get; set; } = new NamedAction("wake");
        public DelayedAction?   OnIdle          { get; set; }

        public DelayedAction?   OnStart         { get; set; }
        public DelayedAction?   OnSuspend       { get; set; }
        public DelayedAction?   OnStop          { get; set; }

        public NamedAction?     OnMagicPacket   { get; set; }

        // Services
        public IList<ServiceInfo> Service { get; set; } = [];
        public IList<HTTPServiceInfo> HTTPService { get; set; } = [];
        public IEnumerable<ServiceInfo> Services => Service.Concat(HTTPService);

        // Filter-Rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; set; } = [];
        public IEnumerable<ServiceFilterRuleInfo> ServiceFilterRules => ServiceFilterRule.Concat(HTTPFilterRule);
        public IList<ServiceFilterRuleInfo> ServiceFilterRule { get; set; } = [];
        public IList<HTTPFilterRuleInfo> HTTPFilterRule { get; set; } = [];
        public PingFilterRuleInfo? PingFilterRule { get; set; }
    }
}
