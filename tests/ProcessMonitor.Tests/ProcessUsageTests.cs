using MadWizard.Desomnia.Configuration;
using System.Globalization;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// The usage token's rendering contract: only what was measured appears, each part in the
    /// unit its threshold compared – a share as a percentage, an amount in bytes, a rate per
    /// second – in the fixed order CPU, GPU, IO, Tx.
    /// </summary>
    public class ProcessUsageTests
    {
        public ProcessUsageTests()
        {
            // the formats under test carry decimal separators; the assertion strings are invariant
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        [Fact]
        public void WithoutThresholds_RendersBare()
        {
            Assert.Equal("{Browser}", new ProcessUsage("Browser").ToString());
        }

        [Fact]
        public void EveryPart_RendersInItsUnit()
        {
            var usage = new ProcessUsage("Steam")
            {
                Metrics = new()
                {
                    ProcessingUsage = 0.20,
                    GraphicsProcessingUsage = 0.05,
                    StorageRate = 4.0 * (1L << 20),  // storage is spoken of in bytes...
                    TrafficRate = 10.0 * (1L << 10) / 8,  // ...and a line speed in bits
                }
            };

            Assert.Equal("{Steam @ CPU=20% | GPU=5% | IO=4.0MB/s | Tx=10kbit/s}", usage.ToString());
        }

        [Fact]
        public void AbsoluteThresholds_RenderAmounts()
        {
            // an amount stays bytes on both counters: only a speed is quoted in bits
            var usage = new ProcessUsage("Backup")
            {
                Metrics = new()
                {
                    ProcessingTime = TimeSpan.FromSeconds(1),
                    Storage = 100L << 20,
                    Traffic = 512,
                }
            };

            Assert.Equal("{Backup @ CPU=00:00:01 | IO=100.0MB | Tx=512B}", usage.ToString());
        }

        [Fact]
        public void TrafficRate_RoundTripsTheThresholdItWasComparedAgainst()
        {
            // "5Mbit/s" configured, exactly 5Mbit/s measured – the log has to say so, which it
            // only does while the formatter steps in the same binary thousands the parser reads
            var threshold = (TransmissionThreshold)new IOThresholdConverter().ConvertFromInvariantString("5Mbit/s")!;

            var usage = new ProcessUsage("Stream") { Metrics = new() { TrafficRate = threshold.Amount * threshold.ByteUnit!.Value } };

            Assert.Equal("{Stream @ Tx=5Mbit/s}", usage.ToString());
        }

        [Fact]
        public void GraphicsTime_RendersLikeTheProcessorTime()
        {
            var usage = new ProcessUsage("Game") { Metrics = new() { GraphicsProcessingTime = TimeSpan.FromSeconds(2) } };

            Assert.Equal("{Game @ GPU=00:00:02}", usage.ToString());
        }

        [Fact]
        public void FractionalShares_KeepOneDecimal()
        {
            var usage = new ProcessUsage("Browser") { Metrics = new() { ProcessingUsage = 0.123 } };

            Assert.Equal("{Browser @ CPU=12.3%}", usage.ToString());
        }
    }
}
