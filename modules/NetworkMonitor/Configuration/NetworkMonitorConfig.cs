using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Configuration.Converter;
using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Configuration.Hosts;
using MadWizard.Desomnia.Network.Configuration.Knocking;
using MadWizard.Desomnia.Network.Configuration.Options;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Configuration
{
    public class NetworkMonitorConfig : LocalHostInfo
    {
        // Network-Identification
        public required string  Name            { get; set; }
        public string?          Interface       { get; set; }
        public IPNetwork?       Network         { get; set; }

        public bool             UseBPF          { get; set; } = true;
        public TimeSpan?        DeviceTimeout   { get; set; } = null;

        // Actions
        public DelayedAction?   OnConnect       { get; set; }
        public NamedAction?     OnDisconnect    { get; set; }

        // Options
        #region Network :: WatchOptions
        internal WatchMode  WatchMode           { get; set; } = WatchMode.Normal;
        internal ushort?    WatchUDPPort        { get; set; } = null;
        internal TimeSpan?  WatchDeviceTimeout  { get; set; } = null;
        internal bool       WatchYield          { get; set; } = false;

        public WatchOptions MakeWatchOptions() => new()
        {
            Mode        = this.WatchMode,
            UDPPorts    = this.WatchUDPPort != null ? [ this.WatchUDPPort.Value ] : [],
            Yield       = this.WatchYield
        };
        #endregion

        #region Network :: AutoDiscoveryOptions
        public AutoDiscoveryType    AutoDetect  { get; set; } = AutoDiscoveryType.None;
        private TimeSpan            AutoTimeout { get; set; } = TimeSpan.FromSeconds(5);
        private TimeSpan?           AutoLatency { get; set; }

        public DiscoveryOptions MakeAutoDiscoveryOptions() => new()
        {
            Timeout = this.AutoTimeout,
            Latency = this.AutoLatency
        };
        #endregion

        #region Network :: SweepOptions
        private TimeSpan    SweepFrequency      { get; set; } = TimeSpan.FromMinutes(1);
        private TimeSpan    SweepDelay          { get; set; } = TimeSpan.FromMinutes(5);

        public SweepOptions MakeSweepOptions() => new()
        {
            Frequency = this.SweepFrequency,
            Delay = this.SweepDelay
        };
        #endregion

        #region Network :: DemandOptions 
        internal TimeSpan   DemandTimeout       { get; set; } = TimeSpan.FromSeconds(5);
        internal bool       DemandForward       { get; set; } = true;
        internal int        DemandParallel      { get; set; } = 1;

        internal AddressAdvertisment Advertise  { get; set; } = AddressAdvertisment.Lazy;
        internal bool AdvertiseIfStopped        { get; set; } = true;
        #endregion

        #region Network :: KnockOptions
        internal string     KnockMethod         { get; set; } = "plain";

        internal ushort     KnockPort           { get; set; } = 62201;
        internal IPProtocol KnockProtocol       { get; set; } = IPProtocol.UDP;

        internal TimeSpan   KnockDelay          { get; set; } = TimeSpan.FromSeconds(0.5);
        internal TimeSpan?  KnockRepeat         { get; set; }
        internal TimeSpan   KnockTimeout        { get; set; } = TimeSpan.FromSeconds(10);
        // Network :: KnockSecret
        internal string?    KnockSecret         { get; set; }
        internal string?    KnockSecretAuth     { get; set; }
        internal string     KnockEncoding       { get; set; } = "UTF-8";
        #endregion

        #region Network :: WakeOptions
        internal WakeType   WakeType            { get; set; } = WakeType.Auto;
        internal ushort     WakePort            { get; set; } = 9;
        internal TimeSpan   WakeTimeout         { get; set; } = TimeSpan.FromSeconds(10);
        internal TimeSpan?  WakeRepeat          { get; set; }
        #endregion

        #region Network :: PingOptions
        internal TimeSpan   PingTimeout         { get; set; } = TimeSpan.FromMilliseconds(500);
        internal TimeSpan?  PingFrequency       { get; set; }
        #endregion

        // Hosts
        public LocalHostInfo?                   LocalHost   { get; private set; }
        public IList<RemotePhysicalHostInfo>    RemoteHost  { get; private set; } = [];
        public IList<NetworkRouterInfo>         Router      { get; private set; } = [];
        public IList<NetworkHostInfo>           Host        { get; private set; } = [];

        // Host-Ranges
        public IList<NetworkHostRangeInfo>          HostRange           { get; private set; } = [];
        public IList<DynamicHostRangeInfo>          DynamicHostRange    { get; private set; } = [];

        public IEnumerable<NetworkHostRangeInfo>    Ranges => HostRange.Concat(DynamicHostRange).Concat(ForeignHostFilterRule?.DynamicHostRange ?? []);

        // Filter-Rules (networkwide)
        public ForeignHostFilterRuleInfo? ForeignHostFilterRule { get; set; }
        public IEnumerable<ServiceFilterRuleInfo> ServiceFilterRules => ServiceFilterRule.Concat(HTTPFilterRule);
        public IList<ServiceFilterRuleInfo> ServiceFilterRule { get; set; } = [];
        public IList<HTTPFilterRuleInfo> HTTPFilterRule { get; set; } = [];
        public PingFilterRuleInfo? PingFilterRule { get; set; }

        static NetworkMonitorConfig() // we want to use native types
        {
            TypeDescriptor.AddAttributes(typeof(PhysicalAddress),   new TypeConverterAttribute(typeof(PhysicalAddressConverter)));
            TypeDescriptor.AddAttributes(typeof(IPAddress),         new TypeConverterAttribute(typeof(IPAddressConverter)));
            TypeDescriptor.AddAttributes(typeof(IPNetwork),         new TypeConverterAttribute(typeof(IPNetworkConverter)));
        }
    }
}
