using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using System.Net;
using System.Text;

namespace MadWizard.Desomnia.Network.Configuration.Services
{
    public class WatchedServiceInfo() : ServiceInfo()
    {
        public IOThreshold? MinTraffic { get; set; }

        // Options
        #region                 AdvertiseOptions
        internal AdvertiseType? Advertise           { get; set; }

        internal TimeSpan?      AdvertiseTimeout    { get; set; }

        internal TimeSpan?      AdvertiseHostTTL    { get; set; }
        internal TimeSpan?      AdvertiseServiceTTL { get; set; }

        public virtual AdvertiseOptions MakeAdvertiseOptions()
        {
            if (Advertise != null)
            {
                return new() // wird von network -> host -> service übertragen
                {
                    Type = Advertise ?? throw new NullReferenceException("Advertise"),
                    Timeout = AdvertiseTimeout ?? throw new NullReferenceException("AdvertiseTimeout"),

                    HostTTL = AdvertiseHostTTL,
                    ServiceTTL = AdvertiseServiceTTL,
                };
            }

            return default;
        }
        #endregion

        #region HandoffOptions
        internal bool           Handoff             { get; set; } = true;
        #endregion

        #region                 KnockOptions
        internal string?        KnockMethod         { get; set; }

        internal IPProtocol?    KnockProtocol       { get; set; }
        internal ushort?        KnockPort           { get; set; }

        internal TimeSpan?      KnockDelay          { get; set; }
        internal TimeSpan?      KnockRepeat         { get; set; }
        internal TimeSpan?      KnockTimeout        { get; set; }

        //                      KnockSecret
        internal string?        KnockSecret         { get; set; }
        internal string?        KnockSecretAuth     { get; set; }
        internal DigestType?    KnockSecretAuthType { get; set; }
        internal Encoding?      KnockSecretEncoding { get; set; }

        public KnockOptions? MakeKnockOptions()
        {
            if (KnockMethod != null)
                return new() // wird von network -> remote host -> service übertragen
                {
                    Method = KnockMethod    ?? throw new NullReferenceException("knockMethod"),

                    Port = new(
                        KnockProtocol       ?? throw new NullReferenceException("knockProtocol"),
                        KnockPort           ?? throw new NullReferenceException("knockPort")),

                    Delay = KnockDelay      ?? throw new NullReferenceException("knockDelay"),
                    Repeat = KnockRepeat,
                    Timeout = KnockTimeout  ?? throw new NullReferenceException("knockTimeout"),

                    Secret = new(
                        KnockSecret, 
                        KnockSecretAuth,
                        KnockSecretAuthType ?? throw new NullReferenceException("knockSecretAuthType"),
                        KnockSecretEncoding ?? throw new NullReferenceException("knockSecretEncoding"))
                };

            return null; // ist kein remote service
        }
        #endregion

        // Events
        public ActionInfo? OnDemand { get; set; }
        public DelayedActionInfo? OnIdle { get; set; }

        // Filter-Rules
        public IList<HostFilterRuleInfo> HostFilterRule { get; set; } = [];
        public IList<HostRangeFilterRuleInfo> HostRangeFilterRule { get; init; } = [];

        public static implicit operator ServiceFilterRuleInfo(WatchedServiceInfo info) => info.ToFilterRule();

        public virtual ServiceFilterRuleInfo ToFilterRule()
        {
            return new ServiceFilterRuleInfo
            {
                Name = Name,
                Protocol = Protocol,
                Port = Port,

                HostFilterRule = HostFilterRule,
                HostRangeFilterRule = HostRangeFilterRule,

                Type = FilterRuleType.Must
            };
        }
    }
}
