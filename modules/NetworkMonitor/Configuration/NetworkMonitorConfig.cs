using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Converter;
using MadWizard.Desomnia.Network.Configuration.Converter;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Knocking.Secrets;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.SleepProxy;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace MadWizard.Desomnia.Network.Configuration
{
    public class NetworkMonitorConfig : LocalHostInfo, IIEnumerable<NetworkHostInfo>
    {
        const long DEFAULT_TIMEOUT_MS = 500;

        /// <summary>
        /// The 0-based position of this network in its <see cref="ModuleConfig{T}.NetworkMonitor"/>
        /// list — the identity that correlates the module's and the plugins' views of the same
        /// configuration: every view binds the same sections in document order, so the ordinal
        /// is the same in each (a name may not be written at all). Stamped by the list.
        /// </summary>
        internal int Ordinal { get; set; } = -1;

        // Network-Identification
        public string?          Name                { get; set; }

        public string?          Interface           { get; set; }
        public IPNetwork?       Network             { get; set; }
        public string?          SSID                { get; set; }

        public bool             UseBPF              { get; set; } = true;

        public WakeOnLANMode?   AllowWakeOnLAN      { get; set; } = DefaultWakeOnLANMode();

        // Actions
        public DelayedActionInfo?   OnIdle          { get; set; }
        public DelayedActionInfo?   OnUsage         { get; set; }
        public DelayedActionInfo?   OnConnect       { get; set; }
        public ActionInfo?          OnDisconnect    { get; set; }

        // Options
        #region Network :: AutoDiscoveryOptions
        public AutoDiscoveryType    AutoDetect                  { get; set; } = AutoDiscoveryType.Nothing;
        internal TimeSpan           AutoTimeout                 { get; set; } = TimeSpan.FromSeconds(2);
        internal TimeSpan?          AutoRefresh                 { get; set; }
        internal bool               AutoParallel                { get; set; } = true;

        public DiscoveryOptions MakeAutoDiscoveryOptions() => new()
        {
            Timeout = this.AutoTimeout,
            Refresh = this.AutoRefresh,
            Parallel = this.AutoParallel
        };
        #endregion

        #region Network :: SweepOptions
        private TimeSpan            SweepFrequency              { get; set; } = TimeSpan.FromMinutes(1);
        private TimeSpan            SweepDelay                  { get; set; } = TimeSpan.FromMinutes(5);

        public SweepOptions MakeSweepOptions() => new()
        {
            Frequency = this.SweepFrequency,
            Delay = this.SweepDelay
        };
        #endregion

        #region Network :: DemandOptions 
        internal DemandSource       DemandSource            { get; set; } = DemandSource.Host;
        internal TimeSpan           DemandTimeout           { get; set; } = TimeSpan.FromSeconds(5);
        internal bool               DemandForward           { get; set; } = true;
        internal int                DemandParallel          { get; set; } = 1;
        #endregion

        #region Network :: AdvertiseOptions 
        internal AdvertiseType      Advertise                   { get; set; } = AdvertiseType.Lazy;
        internal bool?              AdvertiseHosts              { get; set; }
        internal bool?              AdvertiseServices           { get; set; }
        internal bool               AdvertiseIfStopped          { get; set; } = true;
        internal bool               AdvertiseUnicast            { get; set; } = false;

        internal TimeSpan           AdvertiseTimeout            { get; set; } = TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS);

        internal TimeSpan?          AdvertiseHostTTL            { get; set; }
        internal TimeSpan?          AdvertiseServiceTTL         { get; set; }
        #endregion

        #region Network :: HandoffOptions
        internal HandoffType        Handoff                     { get; set; } = HandoffType.None;
        internal TimeSpan           HandoffDuration             { get; set; } = TimeSpan.FromDays(1);
        internal TimeSpan           HandoffTimeout              { get; set; } = TimeSpan.FromSeconds(5);
        internal int                HandoffRetry                { get; set; } = 2;
        internal ushort?            HandoffMTU                  { get; set; }

        internal string?            HandoffPassword             { get; set; }
        internal Encoding           HandoffPasswordEncoding     { get; set; } = Encoding.ASCII;
        #endregion

        #region Network :: SleepProxyOptions
        internal int                SleepProxyLeaseLimit        { get; set; } = 100;
        internal TimeSpan           SleepProxyLeaseDurationMin  { get; set; } = TimeSpan.FromMinutes(30);
        internal TimeSpan           SleepProxyLeaseDurationMax  { get; set; } = TimeSpan.FromDays(365);
        internal LeaseExpireAction  SleepProxyLeaseExpire       { get; set; } = LeaseExpireAction.Wake; // shall we wake host, when lease ends?

        internal SleepProxyDiscoveryType SleepProxyDiscovery    { get; set; } = SleepProxyDiscoveryType.Eager; // default: lazy or eager?

        internal SleepProxyMetrics  SleepProxyMetrics           { get; set; } = SleepProxyMetrics.Best;
        internal ushort?            SleepProxyPort              { get; set; } // null = use an ephemeral port

        public SleepProxyOptions MakeSleepProxyOptions() => new()
        {
            LeaseLimit = SleepProxyLeaseLimit,
            LeaseDurationMin = SleepProxyLeaseDurationMin,
            LeaseDurationMax = SleepProxyLeaseDurationMax,
            LeaseExpire = SleepProxyLeaseExpire,
        };
        #endregion

        #region Network :: KnockOptions
        internal string             KnockMethod                 { get; set; } = "plain";

        internal ushort             KnockPort                   { get; set; } = 62201;
        internal IPProtocol         KnockProtocol               { get; set; } = IPProtocol.UDP;

        internal TimeSpan           KnockDelay                  { get; set; } = TimeSpan.FromSeconds(0.5);
        internal TimeSpan?          KnockRepeat                 { get; set; }
        internal TimeSpan           KnockTimeout                { get; set; } = TimeSpan.FromSeconds(10);
        // Network ::               KnockSecret
        internal string?            KnockSecret                 { get; set; }
        internal string?            KnockSecretAuth             { get; set; }
        internal DigestType         KnockSecretAuthType         { get; set; } = default;
        internal Encoding           KnockSecretEncoding         { get; set; } = Encoding.UTF8;
        #endregion

        #region Network :: PingOptions
        internal TimeSpan           PingTimeout             { get; set; } = TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS);
        internal TimeSpan?          PingFrequency           { get; set; }
        #endregion

        #region Network :: WakeOptions
        internal WakeType           WakeType                    { get; set; } = WakeType.Auto;
        internal ushort             WakePort                    { get; set; } = 9;
        internal string?            WakePassword                { get; set; }
        internal Encoding           WakePasswordEncoding        { get; set; } = Encoding.ASCII;
        internal TimeSpan           WakeTimeout                 { get; set; } = TimeSpan.FromSeconds(15);
        internal TimeSpan?          WakeRepeat                  { get; set; }
        internal bool               WakePing                    { get; set; } = false;
        #endregion

        #region Network :: WatchOptions
        internal WatchMode          WatchMode                   { get; set; } = WatchMode.Normal;
        internal TimeSpan?          WatchTimeout                { get; set; } = null; // capturing should be stable now
        internal ushort?            WatchUDPPort                { get; set; } = null;

        public WatchOptions MakeWatchOptions() => new()
        {
            Mode = this.WatchMode,
            Timeout = this.WatchTimeout,
            UDPPorts = this.WatchUDPPort != null ? [this.WatchUDPPort.Value] : [],
        };
        #endregion

        #region Network :: RouterOptions
        /*
         * Network-wide router defaults ("Router" prefix — the unprefixed names are taken by the
         * host-level options above). A NetworkRouterInfo overrides them only where it sets a value
         * explicitly; see NetworkRouterInfo.MakeRouterOptions.
         */
        internal bool               RouterAllowWake             { get; set; } = false;
        internal bool?              RouterAllowWakeByProxy      { get; set; }
        internal bool               RouterAllowWakeOnLAN        { get; set; } = true;

        internal TimeSpan           RouterVPNTimeout            { get; set; } = TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS);

        /// <summary>Tthe configuration deliberately opens this network to requests from
        /// outside it: a single-instance <see cref="ForeignHostFilterRule"/> or
        /// <see cref="EveryHostFilterRule"/> is that opt-in. Their presence makes proxy-waking the
        /// sensible default for an otherwise-unconfigured router (zero-config remote access).</summary>

        #endregion

        /// <summary>
        /// Interfaces to keep blocked while this network is being monitored — the blocks are
        /// lifted when this monitor shuts down. See <see cref="NetworkInterfaceBlockInfo"/>.
        /// </summary>
        public IList<NetworkInterfaceBlockInfo> NetworkInterfaceBlock { get; private set; } = [];

        // Hosts
        public LocalHostInfo?                   LocalHost   { get; private set; }
        public IList<RemotePhysicalHostInfo>    RemoteHost  { get; private set; } = [];
        public IList<NetworkSleepProxyInfo>     SleepProxy  { get; private set; } = [];
        public IList<NetworkRouterInfo>         Router      { get; private set; } = [];
        public IList<NetworkHostInfo>           Host        { get; private set; } = [];

        // Host-Ranges
        public IList<NetworkHostRangeInfo>      HostRange           { get; private set; } = [];
        public IList<DynamicHostRangeInfo>      DynamicHostRange    { get; private set; } = [];

        // Filter-Rules (networkwide)
        public EveryHostFilterRuleInfo? EveryHostFilterRule { get; set; }
        public ForeignHostFilterRuleInfo? ForeignHostFilterRule { get; set; }
        public IEnumerable<ServiceFilterRuleInfo> ServiceFilterRules => ServiceFilterRule.Concat(HTTPFilterRule);
        public IList<ServiceFilterRuleInfo> ServiceFilterRule { get; set; } = [];
        public IList<HTTPFilterRuleInfo> HTTPFilterRule { get; set; } = [];
        public PingFilterRuleInfo? PingFilterRule { get; set; }

        private static WakeOnLANMode? DefaultWakeOnLANMode()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
               return WakeOnLANMode.MagicPacket | WakeOnLANMode.Default; // don't replace existing modes
            }

            return null;
        }

        #region Host(-Range) enumeration
        public bool HasCatchAllHostFilterRule => ForeignHostFilterRule != null || EveryHostFilterRule != null;

        public IEnumerable<NetworkHostRangeInfo> Ranges => HostRange.Concat(DynamicHostRange)
            .Concat(EveryHostFilterRule?.HostRange ?? []).Concat(EveryHostFilterRule?.DynamicHostRange ?? [])
            .Concat(ForeignHostFilterRule?.HostRange ?? []).Concat(ForeignHostFilterRule?.DynamicHostRange ?? []);

        /// <returns>All configured simple hosts.</returns>
        public IEnumerable<NetworkHostInfo> Hosts => Host
            .Concat(SleepProxy)
            .Concat(Ranges.SelectMany(range => range.Host)) // all hosts in all host ranges
            .Concat(EveryHostFilterRule?.Host ?? [])
            .Concat(ForeignHostFilterRule?.Host ?? []);

        /// <returns>All configured hosts, regardless of type.</returns>
        IEnumerator<NetworkHostInfo> IEnumerable<NetworkHostInfo>.GetEnumerator() => Hosts
            .Concat(Router)
            .Concat(RemoteHost)
            .Concat(RemoteHost.SelectMany(r => r.VirtualHost))
            .Concat(LocalHost?.VirtualHost ?? [])
            .Concat(VirtualHost).GetEnumerator();
        #endregion

        internal bool ShouldAdvertiseSleepProxy
        {
            get
            {
                if (WatchMode == WatchMode.Promiscuous)
                {
                    if (AutoDetect.HasFlag(AutoDiscoveryType.Host))
                        return true;
                    if (AutoDetect.HasFlag(AutoDiscoveryType.Service))
                        return true;

                    foreach (var host in this.OfType<WatchedHostInfo>())
                    {
                        if (host.AutoDetect?.HasFlag(AutoDiscoveryType.Host) ?? false)
                            return true;
                        if (host.AutoDetect?.HasFlag(AutoDiscoveryType.Service) ?? false)
                            return true;
                    }
                }

                return false;
            }
        }

        static NetworkMonitorConfig() // we want to use native types
        {
            TypeDescriptor.AddAttributes(typeof(PhysicalAddress),   new TypeConverterAttribute(typeof(PhysicalAddressConverter)));
            TypeDescriptor.AddAttributes(typeof(IPAddress),         new TypeConverterAttribute(typeof(IPAddressConverter)));
            TypeDescriptor.AddAttributes(typeof(IPNetwork),         new TypeConverterAttribute(typeof(IPNetworkConverter)));
            TypeDescriptor.AddAttributes(typeof(Encoding),          new TypeConverterAttribute(typeof(EncodingConverter)));
        }
    }
}
