using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Manager;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Interface
{
    public class NetworkConfigSelector
    {
        /// <summary>One matcher per configured network, in configuration order.</summary>
        readonly Dictionary<InterfaceMatcher, NetworkMonitorConfig> _matchers = [];

        /// <summary>
        /// Builds the matchers once, before the first pass — the configuration they are derived
        /// from does not change while the application runs.
        /// </summary>
        public NetworkConfigSelector(Func<InterfaceMatcher> CreateMatcher, IEnumerable<NetworkMonitorConfig> configs)
        {
            foreach (var config in configs)
            {
                var matcher = CreateMatcher()
                    .WithType(NetworkInterfaceType.Ethernet, NetworkInterfaceType.Wireless80211)
                    .WithStatus(OperationalStatus.Up)
                    .WithInterface(config.Interface)
                    .WithNetwork(config.Network)
                    .WithSSID(config.SSID)
                    // without a criterion of its own, a network is whichever one carries the default route
                    .WithGateway(config.Interface is null && config.Network is null && config.SSID is null);

                _matchers[matcher] = config;
            }
        }

        internal IEnumerable<NetworkMonitorConfig> ByInterface(INetworkInterface @interface)
        {
            foreach (var (matcher, config) in _matchers)
            {
                if (matcher.Matches(@interface))
                    yield return config;
            }
        }
    }
}
