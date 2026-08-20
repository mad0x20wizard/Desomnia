using MadWizard.Desomnia.Network.Configuration.Options;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class NetworkRouterInfo : NetworkHostInfo
    {
        // Options
        #region                         RouterOptions
        internal protected bool?        AllowWake               { get; set; }
        internal protected bool?        AllowWakeByProxy        { get; set; }
        internal protected bool?        AllowWakeOnLAN          { get; set; }

        internal protected TimeSpan?    VPNTimeout              { get; set; }

        public RouterOptions MakeRouterOptions(NetworkMonitorConfig network) => new()
        {
            AllowWake           = AllowWake             ?? network.RouterAllowWake,
            AllowWakeByProxy    = AllowWakeByProxy      ?? network.RouterAllowWakeByProxy ?? network.HasCatchAllHostFilterRule,
            AllowWakeOnLAN      = AllowWakeOnLAN        ?? network.RouterAllowWakeOnLAN,

            VPNTimeout          = VPNTimeout            ?? network.RouterVPNTimeout,
        };
        #endregion

        public IList<NetworkHostInfo> VPNClient { get; set; } = [];
    }
}
