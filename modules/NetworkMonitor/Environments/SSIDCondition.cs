using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Manager;
using System.Text;

namespace MadWizard.Desomnia.Network.Environments
{
    /// <summary>
    /// Requires the machine to be joined to a designated wireless network (ssid="Kitchen WiFi") —
    /// satisfied while any of its interfaces is associated with that network.
    ///
    /// The name is compared verbatim rather than as a pattern, unlike the "interface" notation:
    /// an SSID is an opaque name picked by the access point and routinely contains characters a
    /// regex would read as syntax ("AVM FRITZ!Box (5 GHz)").
    ///
    /// The wireless name itself is read off the interface, which only a platform whose
    /// <see cref="INetworkInterfaceManager"/> can see wireless information answers — elsewhere
    /// evaluating the condition throws. The platform hosts, not this module, register "ssid".
    /// </summary>
    public sealed class SSIDCondition : NetworkChangeCondition
    {
        /// <summary>The longest SSID 802.11 allows (DOT11_SSID_MAX_LENGTH).</summary>
        const int MAX_LENGTH = 32;

        public SSIDCondition(InterfaceMatcher matcher, string value) : base(matcher)
        {
            if (string.IsNullOrEmpty(value))
                throw new ConfigurationValueException("Invalid ssid; the network name must not be empty.");

            if (Encoding.UTF8.GetByteCount(value) > MAX_LENGTH)
                throw new ConfigurationValueException($"Invalid ssid '{value}'; " +
                    $"a network name is at most {MAX_LENGTH} bytes long.");

            matcher.SSID = value;
        }
    }
}
