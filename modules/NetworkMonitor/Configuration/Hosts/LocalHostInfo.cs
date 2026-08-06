using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;
using System.Text;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class LocalHostInfo
    {
        public TransmissionThreshold? MinTraffic { get; set; }

        // Options
        #region         DemandOptions
        DemandSource?   DemandSource        { get; set; }
        TimeSpan?       DemandTimeout       { get; set; }
        int?            DemandParallel      { get; set; }

        public DemandOptions MakeDemandOptions(NetworkMonitorConfig network) => new()
        {
            Source      = DemandSource      ?? network.DemandSource,
            Timeout     = DemandTimeout     ?? network.DemandTimeout,
            Parallel    = DemandParallel    ?? network.DemandParallel,
            Forward     = false
        };
        #endregion

        #region AdvertiseOptions
        private bool?       AdvertiseServices       { get; set; }

        private TimeSpan?   AdvertiseTimeout        { get; set; }

        private TimeSpan?   AdvertiseHostTTL        { get; set; }
        private TimeSpan?   AdvertiseServiceTTL     { get; set; }

        public AdvertiseOptions MakeAdvertiseOptions(NetworkMonitorConfig network) => new()
        {
            Type        = AdvertiseType.Never

                        | (AdvertiseServices    ?? network.AdvertiseServices ?? false ? AdvertiseType.Service : 0),

            Timeout     = AdvertiseTimeout      ?? network.AdvertiseTimeout,

            HostTTL     = AdvertiseHostTTL      ?? network.AdvertiseHostTTL,
            ServiceTTL  = AdvertiseServiceTTL   ?? network.AdvertiseServiceTTL,
        };
        #endregion

        #region             HandoffOptions
        HandoffType?        Handoff                 { get; set; }
        TimeSpan?           HandoffDuration         { get; set; }
        TimeSpan?           HandoffTimeout          { get; set; }
        int?                HandoffRetry            { get; set; }
        ushort?             HandoffMTU              { get; set; }

        string?             HandoffPassword         { get; set; }
        byte[]?             HandoffPasswordBytes    { get; set; }
        Encoding?           HandoffPasswordEncoding { get; set; }

        public virtual HandoffOptions MakeHandoffOptions(NetworkMonitorConfig network)
        {
            if ((HandoffPassword ?? network.HandoffPassword) is string password)
            {
                HandoffPasswordBytes ??= (HandoffPasswordEncoding ?? network.HandoffPasswordEncoding).GetBytes(password);
            }

            return new()
            {
                Type = Handoff ?? network.Handoff,
                Duration = HandoffDuration ?? network.HandoffDuration,
                Timeout = HandoffTimeout ?? network.HandoffTimeout,
                Retry = HandoffRetry ?? network.HandoffRetry,
                MTU = HandoffMTU ?? network.HandoffMTU,

                Password = HandoffPasswordBytes
            };
        }
        #endregion

        // Ports
        public IList<WatchedServiceInfo> Service { get; set; } = [];
        public IList<WatchedHTTPServiceInfo> HTTPService { get; set; } = [];
        public IEnumerable<WatchedServiceInfo> Services => Service.Concat(HTTPService);

        // Virtual-Hosts
        public IList<LocalVirtualHostInfo> VirtualHost { get; set; } = [];

        // Filter-Rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; set; } = [];
    }
}
