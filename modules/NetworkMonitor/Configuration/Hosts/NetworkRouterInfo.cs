using MadWizard.Desomnia.Network.Configuration.Options;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class NetworkRouterInfo : NetworkHostInfo
    {
        public IList<NetworkHostInfo> VPNClient { get; set; } = [];

        private bool AllowWake { get; set; } = false;
        private bool AllowWakeByProxy { get; set; } = false;
        private bool AllowWakeOnLAN { get; set; } = true;

        private TimeSpan VPNTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

        public RouterOptions Options => new()
        {
            AllowWake = AllowWake,
            AllowWakeByProxy = AllowWakeByProxy,
            AllowWakeOnLAN = AllowWakeOnLAN,

            VPNTimeout = VPNTimeout
        };
    }
}
