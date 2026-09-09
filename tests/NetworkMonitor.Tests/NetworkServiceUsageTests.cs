using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;
using System.Globalization;
using Xunit;

namespace MadWizard.Desomnia.Network.Tests
{
    /// <summary>
    /// What a service reports having done. A threshold that asked about a speed is answered with
    /// one – in bits, the way a line is quoted – while one that asked about an amount, or about
    /// packets, leaves the service to speak for itself as it always did.
    /// </summary>
    public class NetworkServiceUsageTests
    {
        public NetworkServiceUsageTests()
        {
            // the rendered decimal separator is the running culture's; the assertions are invariant
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        /// <summary>A service in name only – nothing here inspects a packet.</summary>
        private sealed class NamedService(string name) : NetworkService(name)
        {
            public override bool Accepts(Packet packet, PacketDirection direction) => throw new Xunit.Sdk.XunitException("no packet should be inspected here");
        }

        private static NetworkService Service(string name) => new NamedService(name);

        [Fact]
        public void WithoutARate_TheServiceSpeaksForItself()
        {
            var usage = new NetworkServiceUsage(Service("Sunshine"), 1024);

            Assert.Equal("Sunshine", usage.ToString());
        }

        [Fact]
        public void WithARate_ItIsQuotedInBitsPerSecond()
        {
            // 655360 B/s = 5 Mbit/s in the same binary thousands the threshold grammar reads
            var usage = new NetworkServiceUsage(Service("Sunshine"), 1966080) { Rate = 655360 };

            Assert.Equal("Sunshine@5Mbit/s", usage.ToString());
        }

        [Fact]
        public void TheRate_RoundTripsTheThresholdItWasComparedAgainst()
        {
            // what "5Mbit/s" asks for, measured exactly, has to read back as "5Mbit/s" – otherwise
            // the log looks like a different measurement than the one the user configured
            var threshold = (TransmissionThreshold)new IOThresholdConverter().ConvertFromInvariantString("5Mbit/s")!;

            var usage = new NetworkServiceUsage(Service("Sunshine"), 0) { Rate = threshold.Amount * threshold.ByteUnit!.Value };

            Assert.Equal("Sunshine@5Mbit/s", usage.ToString());
        }

        [Fact]
        public void SmallRates_KeepTheirMagnitude()
        {
            var usage = new NetworkServiceUsage(Service("mDNS"), 0) { Rate = 1280 }; // 10 kbit/s

            Assert.Equal("mDNS@10kbit/s", usage.ToString());
        }
    }
}
