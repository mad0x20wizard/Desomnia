using MadWizard.Desomnia.Network.Manager;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.Interface
{
    /// <summary>
    /// Matches network interfaces against a set of criteria, using the exact same notation as the
    /// NetworkMonitor "interface" and "network" attributes and the environment conditions supplied
    /// by this module.
    ///
    /// Every criterion is optional and an unset one matches anything; the ones that are set are
    /// combined with AND. A matcher is meant to be configured once — after the configuration has
    /// been parsed — and then only fed <see cref="INetworkInterface"/> instances.
    ///
    /// Matching an interface by its name is the one platform-dependent part — the id means
    /// something different on Windows than on the Unixes — so it sits behind a virtual member a
    /// platform host can override; see <see cref="MatchesInterface"/>. Matching by SSID reads the
    /// wireless name straight off <see cref="INetworkInterface.SSID"/>, which answers it wherever
    /// the platform can and throws where it cannot.
    /// </summary>
    public class InterfaceMatcher
    {
        public InterfaceMatcher() { }

        public InterfaceMatcher(string? @interface) => Interface = @interface;

        public InterfaceMatcher(IPNetwork network) => Network = network;

        public InterfaceMatcher(string? @interface, IPNetwork? network)
        {
            Interface = @interface;
            Network = network;
        }

        #region Criteria
        /// <summary>
        /// The interface to match, in the notation of the "interface" attribute. Interpreted by
        /// <see cref="MatchesInterface"/> — an unanchored regex against the interface id here,
        /// possibly more than that on a platform that has more to offer.
        /// </summary>
        public string? Interface { get; set; }

        /// <summary>The network one of the interface's addresses has to lie in.</summary>
        public IPNetwork? Network { get; set; }

        /// <summary>The accepted operational states; null accepts any (mere presence).</summary>
        public IReadOnlySet<OperationalStatus>? Status { get; set; }

        /// <summary>The accepted interface types (a whitelist); null accepts any.</summary>
        public IReadOnlySet<NetworkInterfaceType>? Type { get; set; }

        /// <summary>Whether the interface has to carry a default route.</summary>
        public bool RequireGateway  { get; set; } = false;
        /// <summary>Whether the interface must not have a automatically configured IP.</summary>
        public bool RejectAPIPA     { get; set; } = false;

        /// <summary>
        /// The wireless network the interface has to be joined to, compared verbatim — an SSID is
        /// an opaque name, not a pattern. Evaluating it reads <see cref="INetworkInterface.SSID"/>,
        /// so a configuration that sets it on a platform without wireless information fails when an
        /// interface is matched, not when it is assigned here.
        /// </summary>
        public string? SSID { get; set; }
        #endregion

        #region Fluent configuration
        public InterfaceMatcher WithInterface(string? @interface) { Interface = @interface; return this; }

        public InterfaceMatcher WithNetwork(IPNetwork? network) { Network = network; return this; }

        public InterfaceMatcher WithStatus(params OperationalStatus[] statuses) { Status = new HashSet<OperationalStatus>(statuses); return this; }

        public InterfaceMatcher WithType(params NetworkInterfaceType[] types) { Type = new HashSet<NetworkInterfaceType>(types); return this; }

        public InterfaceMatcher WithGateway(bool required = true) { RequireGateway = required; return this; }

        public InterfaceMatcher WithSSID(string? ssid) { SSID = ssid; return this; }
        #endregion

        /// <summary>Whether the given interface satisfies every criterion that is set.</summary>
        public bool Matches(INetworkInterface @interface)
        {
            // cheapest criteria first - the SSID lookup at the end is a platform call
            if (Type is IReadOnlySet<NetworkInterfaceType> types && !types.Contains(@interface.Type))
                return false;

            if (Status is IReadOnlySet<OperationalStatus> statuses && !statuses.Contains(@interface.Status))
                return false;

            if (Interface is string pattern && !MatchesInterface(@interface, pattern))
                return false;

            if (Network is IPNetwork network && !MatchesNetwork(@interface, network))
                return false;

            if (RequireGateway && !HasGateway(@interface))
                return false;
            if (RejectAPIPA && HasOnlyAPIPA(@interface))
                return false;

            if (SSID is string ssid && !MatchesSSID(@interface, ssid))
                return false;

            return true;
        }

        /// <summary>
        /// Matches the "interface" notation against a single interface. The platform-neutral
        /// implementation is an unanchored regex against <see cref="NetworkIdentity.Id"/>, which
        /// is the only stable handle on Linux and macOS — there the id *is* the interface name
        /// ("en0", "eth0"). On Windows the id is the adapter GUID and the human-readable name
        /// lives in <see cref="INetworkInterface.Name"/>, so the Windows host overrides this.
        /// </summary>
        protected virtual bool MatchesInterface(INetworkInterface @interface, string pattern)
        {
            return Regex.IsMatch(@interface.Identity.Id, pattern);
        }

        protected virtual bool MatchesNetwork(INetworkInterface @interface, IPNetwork network)
        {
            foreach (var unicast in @interface.Addresses)
            {
                if (network.Contains(unicast.Address))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether an interface is joined to <see cref="SSID"/>, compared verbatim. Only a wireless
        /// adapter can be — the type is checked first, which also spares the platform a lookup for
        /// every wired interface. The name itself comes from <see cref="INetworkInterface.SSID"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">The platform exposes no wireless information.</exception>
        protected static bool MatchesSSID(INetworkInterface @interface, string ssid)
        {
            if (@interface.Type != NetworkInterfaceType.Wireless80211)
                return false;

            return string.Equals(@interface.SSID, ssid, StringComparison.Ordinal);
        }

        protected static bool HasGateway(INetworkInterface @interface)
        {
            return @interface.Gateways.Count > 0;
        }

        protected static bool HasOnlyAPIPA(INetworkInterface @interface)
        {
            bool? onlyAPIPA = null;

            foreach (var unicast in @interface.Addresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (unicast.Address.IsAPIPA())
                        onlyAPIPA = true;
                    else
                        return false;
                }
                else
                    continue;
            }

            return onlyAPIPA == true;
        }
    }
}
