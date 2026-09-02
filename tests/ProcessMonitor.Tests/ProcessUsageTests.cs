using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Metrics;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    public class ProcessUsageTests
    {
        [Fact]
        public void WithoutThresholds_RendersBare()
        {
            Assert.Equal("{Browser}", new ProcessUsage("Browser").ToString());
        }

        [Fact]
        public void EveryPart_RendersInItsConfiguredFormat()
        {
            var sample = TimeSpan.FromSeconds(10);
            var usage = new ProcessUsage("Steam")
            {
                Metrics = new(sample)
                {
                    Processor = new(TimeSpan.FromSeconds(2), sample, ProcessingMetricFormat.Percentage),
                    GraphicsProcessor = new(TimeSpan.FromSeconds(0.5), sample, ProcessingMetricFormat.Percentage),
                    Storage = new(40L << 20, TransferMetricFormat.BytesPerSecond),
                    Traffic = new(12800, TransferMetricFormat.BitsPerSecond),
                }
            };

            Assert.Equal("{Steam @ CPU=20% | GPU=5% | IO=4.0MB/s | Tx=10kbit/s}", usage.ToString());
        }

        [Fact]
        public void AbsoluteThresholds_RenderAmounts()
        {
            var sample = TimeSpan.FromSeconds(2);
            var usage = new ProcessUsage("Backup")
            {
                Metrics = new(sample)
                {
                    Processor = new(TimeSpan.FromSeconds(1), sample, ProcessingMetricFormat.Time),
                    Storage = new(100L << 20, TransferMetricFormat.Bytes),
                    Traffic = new(512, TransferMetricFormat.Bytes),
                }
            };

            Assert.Equal("{Backup @ CPU=00:00:01 | IO=100.0MB | Tx=512B}", usage.ToString());
        }

        [Fact]
        public void TrafficRate_RoundTripsTheThresholdItWasComparedAgainst()
        {
            var threshold = (TransmissionThreshold)new IOThresholdConverter().ConvertFromInvariantString("5Mbit/s")!;
            var metrics = new ProcessUsageMetrics(TimeSpan.FromSeconds(1))
            {
                Traffic = new(threshold.Amount * threshold.ByteUnit!.Value, TransferMetricFormat.BitsPerSecond),
            };

            Assert.Equal("{Stream @ Tx=5Mbit/s}", new ProcessUsage("Stream") { Metrics = metrics }.ToString());
        }

        [Fact]
        public void GraphicsTime_RendersLikeProcessorTime()
        {
            var sample = TimeSpan.FromSeconds(5);
            var metrics = new ProcessUsageMetrics(sample)
            {
                GraphicsProcessor = new(TimeSpan.FromSeconds(2), sample, ProcessingMetricFormat.Time),
            };

            Assert.Equal("{Game @ GPU=00:00:02}", new ProcessUsage("Game") { Metrics = metrics }.ToString());
        }

        [Fact]
        public void FractionalShares_KeepOneDecimal()
        {
            var sample = TimeSpan.FromSeconds(10);
            var metrics = new ProcessUsageMetrics(sample)
            {
                Processor = new(TimeSpan.FromSeconds(1.23), sample, ProcessingMetricFormat.Percentage),
            };

            Assert.Equal("{Browser @ CPU=12.3%}", new ProcessUsage("Browser") { Metrics = metrics }.ToString());
        }
    }
}
