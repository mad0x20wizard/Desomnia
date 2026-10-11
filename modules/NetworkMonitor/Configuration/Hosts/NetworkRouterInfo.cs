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
            AllowWakeByProxy    = AllowWakeByProxy      ?? network.RouterAllowWakeByProxy ?? ShouldAllowWakeByProxy(network),
            AllowWakeOnLAN      = AllowWakeOnLAN        ?? network.RouterAllowWakeOnLAN,

            VPNTimeout          = VPNTimeout            ?? network.RouterVPNTimeout,
        };

        private static bool ShouldAllowWakeByProxy(NetworkMonitorConfig network)
        {
            if (((IFilterInfo)network).AnyHostFilterRules.Any())
            {
                return true; // network wide filtering is enabled
            }
            else if (network.LocalHost is null && network.Services.Any())
            {
                return false; // implicit local host is not covered
            }
            else if (network.WatchedHostFilters is { } hosts && hosts.Any())
            {
                return hosts.All(host => host.AnyHostFilterRules.Any()); // do all watched hosts have filters?
            }
            else
            {
                return false; // don't care, don't enable
            }
        }
        #endregion

        public IList<NetworkHostInfo> VPNClient { get; set; } = [];
    }
}
