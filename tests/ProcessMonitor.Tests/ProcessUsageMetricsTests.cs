using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    public class ProcessUsageMetricsTests
    {
        [Fact]
        public void NullLeftOperand_ReturnsRightOperand()
        {
            ProcessUsageMetrics? left = null;
            var right = new ProcessUsageMetrics { ProcessingUsage = 0.5 };

            var result = left + right;

            Assert.Same(right, result);
        }

        [Fact]
        public void ComplementaryValues_AreAllPreserved()
        {
            var left = new ProcessUsageMetrics
            {
                ProcessingUsage = 0.1,
                GraphicsProcessingTime = TimeSpan.FromSeconds(2),
                Storage = 3,
                TrafficRate = 4,
            };
            var right = new ProcessUsageMetrics
            {
                ProcessingTime = TimeSpan.FromSeconds(5),
                GraphicsProcessingUsage = 0.6,
                StorageRate = 7,
                Traffic = 8,
            };

            var result = left + right;

            Assert.Equal(0.1, result.ProcessingUsage);
            Assert.Equal(TimeSpan.FromSeconds(5), result.ProcessingTime);
            Assert.Equal(0.6, result.GraphicsProcessingUsage);
            Assert.Equal(TimeSpan.FromSeconds(2), result.GraphicsProcessingTime);
            Assert.Equal(3, result.Storage);
            Assert.Equal(7, result.StorageRate);
            Assert.Equal(8, result.Traffic);
            Assert.Equal(4, result.TrafficRate);
        }

        [Fact]
        public void OverlappingValues_UseEachMaximum()
        {
            var left = new ProcessUsageMetrics
            {
                ProcessingUsage = 0.7,
                ProcessingTime = TimeSpan.FromSeconds(1),
                GraphicsProcessingUsage = 0.1,
                GraphicsProcessingTime = TimeSpan.FromSeconds(4),
                Storage = 100,
                StorageRate = 500,
                Traffic = 700,
                TrafficRate = 800,
            };
            var right = new ProcessUsageMetrics
            {
                ProcessingUsage = 0.2,
                ProcessingTime = TimeSpan.FromSeconds(2),
                GraphicsProcessingUsage = 0.8,
                GraphicsProcessingTime = TimeSpan.FromSeconds(3),
                Storage = 200,
                StorageRate = 400,
                Traffic = 600,
                TrafficRate = 900,
            };

            var result = left + right;

            Assert.Equal(0.7, result.ProcessingUsage);
            Assert.Equal(TimeSpan.FromSeconds(2), result.ProcessingTime);
            Assert.Equal(0.8, result.GraphicsProcessingUsage);
            Assert.Equal(TimeSpan.FromSeconds(4), result.GraphicsProcessingTime);
            Assert.Equal(200, result.Storage);
            Assert.Equal(500, result.StorageRate);
            Assert.Equal(700, result.Traffic);
            Assert.Equal(900, result.TrafficRate);
        }
    }
}
