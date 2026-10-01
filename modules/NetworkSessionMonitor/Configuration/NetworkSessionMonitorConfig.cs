using MadWizard.Desomnia.NetworkSession.Configuration.Options;

namespace MadWizard.Desomnia.NetworkSession.Configuration
{
    public class NetworkSessionMonitorConfig
    {
        // Options
        #region Network :: WatchOptions
        internal bool WatchPassive              { get; set; } = true;

        public WatchOptions MakeWatchOptions() => new()
        {
            Passive = WatchPassive
        };
        #endregion

        public IEnumerable<NetworkSessionFilterRule> FilterRule { get; set; } = [];
    }
}
