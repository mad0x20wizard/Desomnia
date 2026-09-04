using MadWizard.Desomnia.Processes.Metrics;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    public class ProcessUsageMetricsTests
    {
        private static readonly TimeSpan SampleDuration = TimeSpan.FromSeconds(10);

        [Fact]
        public void NullLeftOperand_ReturnsRightOperand()
        {
            ProcessMetricsUsage? left = null;
            var right = new ProcessMetricsUsage(SampleDuration)
            {
                Processor = new(TimeSpan.FromSeconds(5), SampleDuration, ProcessingMetricFormat.Percentage),
            };

            Assert.Same(right, left | right);
        }

        [Fact]
        public void ComplementaryValues_AreAllPreserved()
        {
            var processor = new ProcessingMetric(TimeSpan.FromSeconds(1), SampleDuration, ProcessingMetricFormat.Percentage);
            var graphics = new ProcessingMetric(TimeSpan.FromSeconds(2), SampleDuration, ProcessingMetricFormat.Time);
            var storage = new TransferMetric(3, TransferMetricFormat.Bytes);
            var traffic = new TransferMetric(4, TransferMetricFormat.BitsPerSecond);

            var left = new ProcessMetricsUsage(SampleDuration) { Processor = processor, Storage = storage };
            var right = new ProcessMetricsUsage(SampleDuration) { GraphicsProcessor = graphics, Traffic = traffic };

            var result = left | right;

            Assert.Equal(SampleDuration, result.SampleDuration);
            Assert.Equal(processor, result.Processor);
            Assert.Equal(graphics, result.GraphicsProcessor);
            Assert.Equal(storage, result.Storage);
            Assert.Equal(traffic, result.Traffic);
        }

        [Fact]
        public void OverlappingValues_KeepTheWholeGreatestMeasurement()
        {
            var left = new ProcessMetricsUsage(SampleDuration)
            {
                Processor = new(TimeSpan.FromSeconds(7), SampleDuration, ProcessingMetricFormat.Percentage),
                GraphicsProcessor = new(TimeSpan.FromSeconds(1), SampleDuration, ProcessingMetricFormat.Time),
                Storage = new(100, TransferMetricFormat.Bytes),
                Traffic = new(700, TransferMetricFormat.Bytes),
            };
            var right = new ProcessMetricsUsage(SampleDuration)
            {
                Processor = new(TimeSpan.FromSeconds(2), SampleDuration, ProcessingMetricFormat.Time),
                GraphicsProcessor = new(TimeSpan.FromSeconds(8), SampleDuration, ProcessingMetricFormat.Percentage),
                Storage = new(200, TransferMetricFormat.BytesPerSecond),
                Traffic = new(600, TransferMetricFormat.BitsPerSecond),
            };

            var result = left | right;

            Assert.Equal(left.Processor, result.Processor);
            Assert.Equal(right.GraphicsProcessor, result.GraphicsProcessor);
            Assert.Equal(right.Storage, result.Storage);
            Assert.Equal(left.Traffic, result.Traffic);
        }

        [Fact]
        public void DifferentSampleDurations_CannotBeCombined()
        {
            var left = new ProcessMetricsUsage(TimeSpan.FromSeconds(1));
            var right = new ProcessMetricsUsage(TimeSpan.FromSeconds(2));

            Assert.Throws<ArgumentException>(() => left | right);
        }
    }
}
