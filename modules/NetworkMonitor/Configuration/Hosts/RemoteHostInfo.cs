using MadWizard.Desomnia.Network.Configuration.Options;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class RemoteHostInfo : WatchedHostInfo
    {
        // Options
        #region     DemandOptions
        bool?       AdvertiseIfStopped { get; set; } = true;

        public override DemandOptions MakeDemandOptions(NetworkMonitorConfig network)
        {
            var options = base.MakeDemandOptions(network);

            if (network.WatchMode != WatchMode.Promiscuous)
            {
                options = options with { Advertise = AddressAdvertisment.Never };
            }
            else if (AdvertiseIfStopped ?? network.AdvertiseIfStopped)
            {
                options = options with { Advertise = options.Advertise | AddressAdvertisment.Stop };
            }

            return options;
        }
        #endregion

        #region                 KnockOptions
        internal string?        KnockMethod     { get; set; }

        internal IPProtocol?    KnockProtocol   { get; set; }
        internal ushort?        KnockPort       { get; set; }

        internal TimeSpan?      KnockDelay      { get; set; }
        internal TimeSpan?      KnockRepeat     { get; set; }
        internal TimeSpan?      KnockTimeout    { get; set; }

        //                      KnockSecret
        internal string?        KnockSecret     { get; set; }
        internal string?        KnockSecretAuth { get; set; }
        internal string?        KnockEncoding   { get; set; }
        #endregion

        #region     WakeOptions
        WakeType?   WakeType        { get; set; }
        ushort?     WakePort        { get; set; }
        TimeSpan?   WakeTimeout     { get; set; }
        TimeSpan?   WakeRepeat      { get; set; }

        bool        WakeSilent      { get; set; }

        public WakeOptions MakeWakeOptions(NetworkMonitorConfig network) => new()
        {
            Type = WakeType ?? network.WakeType,
            Port = WakePort ?? network.WakePort,

            Timeout = WakeTimeout ?? network.WakeTimeout,
            Repeat = WakeRepeat ?? network.WakeRepeat,

            Silent = WakeSilent,
        };
        #endregion

        #region     PingOptions
        TimeSpan?   PingTimeout     { get; set; }
        TimeSpan?   PingFrequency   { get; set; }

        public PingOptions MakePingOptions(NetworkMonitorConfig network) => new()
        {
            Timeout = PingTimeout ?? network.PingTimeout,
            Frequency = PingFrequency ?? network.PingFrequency
        };
        #endregion
    }
}
