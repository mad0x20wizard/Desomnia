using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Configuration.Services;
using System.Text;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class WatchedHostInfo : NetworkHostInfo
    {
        public TransmissionThreshold? MinTraffic { get; set; }

        // Options
        #region                     DemandOptions
        internal DemandSource?      DemandSource            { get; set; }
        internal TimeSpan?          DemandTimeout           { get; set; }
        internal bool?              DemandForward           { get; set; }
        internal int?               DemandParallel          { get; set; }

        public virtual DemandOptions MakeDemandOptions(NetworkMonitorConfig network) => new()
        {
            Source      = DemandSource          ?? network.DemandSource,
            Timeout     = DemandTimeout         ?? network.DemandTimeout,
            Forward     = DemandForward         ?? network.DemandForward,
            Parallel    = DemandParallel        ?? network.DemandParallel,
        };
        #endregion

        #region                     AdvertiseOptions
        internal AdvertiseType?     Advertise               { get; set; }
        internal bool?              AdvertiseHost           { get; set; }
        internal bool?              AdvertiseServices       { get; set; }

        internal TimeSpan?          AdvertiseTimeout        { get; set; }

        internal TimeSpan?          AdvertiseHostTTL        { get; set; }
        internal TimeSpan?          AdvertiseServiceTTL     { get; set; }

        public virtual AdvertiseOptions MakeAdvertiseOptions(NetworkMonitorConfig network) => new()
        {
            Type        = (Advertise            ?? network.Advertise)
                        | (AdvertiseHost        ?? network.AdvertiseHosts       ?? false    ? AdvertiseType.Host    : 0)
                        | (AdvertiseServices    ?? network.AdvertiseServices    ?? false    ? AdvertiseType.Service : 0),

            Timeout     = AdvertiseTimeout      ?? network.AdvertiseTimeout,

            HostTTL     = AdvertiseHostTTL      ?? network.AdvertiseHostTTL,
            ServiceTTL  = AdvertiseServiceTTL   ?? network.AdvertiseServiceTTL,
        };
        #endregion

        #region                     HandoffOptions
        internal HandoffType?       Handoff                 { get; set; }
        internal TimeSpan?          HandoffDuration         { get; set; }
        internal TimeSpan?          HandoffTimeout          { get; set; }
        internal int?               HandoffRetry            { get; set; }
        internal ushort?            HandoffMTU              { get; set; }

        internal string?            HandoffPassword         { get; set; }
        internal byte[]?            HandoffPasswordBytes    { get; set; }
        internal Encoding?          HandoffPasswordEncoding { get; set; }

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

        // Events
        public DelayedActionInfo?   OnServiceUsage          { get; set; }
        public ActionInfo?          OnServiceDemand         { get; set; }
        public DelayedActionInfo?   OnUsage                 { get; set; }
        public ActionInfo?          OnDemand                { get; set; } = new ActionInfo("wake");
        public DelayedActionInfo?   OnIdle                  { get; set; }

        public DelayedActionInfo?   OnStart                 { get; set; }
        public DelayedActionInfo?   OnSuspend               { get; set; }
        public DelayedActionInfo?   OnStop                  { get; set; }

        public ActionInfo?          OnMagicPacket           { get; set; }

        // Services
        public IList<WatchedServiceInfo> Service { get; set; } = [];
        public IList<WatchedHTTPServiceInfo> HTTPService { get; set; } = [];
        public override IEnumerable<WatchedServiceInfo> Services => Service.Concat(HTTPService);

        // Filter-Rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; set; } = [];
        public IEnumerable<ServiceFilterRuleInfo> ServiceFilterRules => ServiceFilterRule.Concat(HTTPFilterRule);
        public IList<ServiceFilterRuleInfo> ServiceFilterRule { get; set; } = [];
        public IList<HTTPFilterRuleInfo> HTTPFilterRule { get; set; } = [];
        public PingFilterRuleInfo? PingFilterRule { get; set; }
    }
}
