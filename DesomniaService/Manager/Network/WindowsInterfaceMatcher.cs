using MadWizard.Desomnia.Network.Interface;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Windows is the one platform where an interface has two names: the id is the adapter GUID
    /// ("{3A1B...}"), the human-readable name the one shown in the network connections folder
    /// ("Ethernet 2", "WiFi"). Nobody writes a GUID into a configuration file, so the notation
    /// accepts either — a regex against the id, or the display name verbatim.
    /// </summary>
    internal sealed class WindowsInterfaceMatcher : InterfaceMatcher
    {
        public WindowsInterfaceMatcher() { }

        public WindowsInterfaceMatcher(string? @interface) : base(@interface) { }

        protected override bool MatchesInterface(INetworkInterface @interface, string pattern)
        {
            if (Regex.IsMatch(@interface.Identity.Id, pattern))
                return true;

            if (@interface.Name.Equals(pattern))
                return true;

            return false;
        }
    }
}
