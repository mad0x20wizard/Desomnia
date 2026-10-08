using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Environments;
using MadWizard.Desomnia.Network.Manager;
using System.Net.NetworkInformation;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    public class SSIDConditionTests
    {
        [Theory]
        [InlineData("")]                                    // no network name at all
        [InlineData("123456789012345678901234567890123")]   // 33 characters - one over the 802.11 limit
        [InlineData("äöüäöüäöüäöüäöüäö1")]                  // 33 bytes in UTF-8, though only 18 characters
        public void InvalidValue_Throws(string value)
        {
            Assert.Throws<ConfigurationValueException>(() => new SSIDCondition(new InterfaceMatcher(), value) { Manager = Wireless(null) });
        }

        [Fact]
        public void MaximumLength_IsAccepted()
        {
            _ = new SSIDCondition(new InterfaceMatcher(), new string('X', 32)) { Manager = Wireless(null) };
        }

        [Fact]
        public void SSID_IsComparedVerbatim()
        {
            // a name full of regex syntax has to be matched as the literal it is
            Assert.True(JoinedTo("AVM FRITZ!Box (5 GHz)", "AVM FRITZ!Box (5 GHz)").IsSatisfied());

            // the same name with the regex-significant characters as wildcards must NOT match
            Assert.False(JoinedTo("AVM FRITZ!Box (5 GHz)", "AVM FRITZ.Box .5 GHz.").IsSatisfied());
        }

        [Fact]
        public void JoinedToNothing_IsNotSatisfied()
        {
            Assert.False(JoinedTo(null, "Kitchen WiFi").IsSatisfied());
        }

        [Fact]
        public void UnsupportedPlatform_ThrowsWhenMatched_NotWhenConfigured()
        {
            // the configuration is accepted without complaint...
            var condition = new SSIDCondition(new InterfaceMatcher(), "Kitchen WiFi")
            {
                Manager = new FakeNetworkInterfaceManager(
                    new NoWirelessInfo("wlan0") { Type = NetworkInterfaceType.Wireless80211 })
            };

            // ...and the platform's lack of wireless information surfaces only once a wireless
            // interface is actually evaluated
            Assert.Throws<NotSupportedException>(() => condition.IsSatisfied());
        }

        /// <summary>A condition against a single wireless interface joined to <paramref name="joined"/>.</summary>
        private static SSIDCondition JoinedTo(string? joined, string wanted)
            => new(new InterfaceMatcher(), wanted) { Manager = Wireless(joined) };

        private static FakeNetworkInterfaceManager Wireless(string? joined) => new(
            new FakeNetworkInterface("wlan0") { Type = NetworkInterfaceType.Wireless80211, SSID = joined });

        /// <summary>A wireless interface on a platform that cannot read the SSID.</summary>
        private sealed class NoWirelessInfo(string id) : FakeNetworkInterface(id)
        {
            public override string? SSID => throw new NotSupportedException();
        }
    }
}
