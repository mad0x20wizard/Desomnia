using MadWizard.Desomnia.Configuration;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    /// <summary>
    /// The one grammar every traffic-shaped threshold parses through: an integer, an optional
    /// byte or bit magnitude, an optional time divisor. Bit units are the byte magnitudes eight
    /// times finer – and a lone "bit" is refused rather than rounded away.
    /// </summary>
    public class IOThresholdConverterTests
    {
        private static TransmissionThreshold Parse(string text)
        {
            return (TransmissionThreshold)new IOThresholdConverter().ConvertFromInvariantString(text)!;
        }

        [Theory]
        [InlineData("500", 500, null)]          // unit-less: packets to the network side, refused by the process side
        [InlineData("100b", 100, 1L)]
        [InlineData("100kb", 100, 1L << 10)]
        [InlineData("1MB", 1, 1L << 20)]
        [InlineData("2GB", 2, 1L << 30)]
        [InlineData("1TB", 1, 1L << 40)]
        [InlineData("5kbit", 5, (1L << 10) / 8)]
        [InlineData("10Mbit", 10, (1L << 20) / 8)]
        [InlineData("10MBit", 10, (1L << 20) / 8)] // every casing parses the same...
        [InlineData("10MBIT", 10, (1L << 20) / 8)]
        [InlineData("10mbit", 10, (1L << 20) / 8)]
        [InlineData("8Gbit", 8, (1L << 30) / 8)]
        [InlineData("1Tbit", 1, (1L << 40) / 8)]
        public void Magnitudes_ParseToBytes(string text, long value, long? unit)
        {
            var threshold = Parse(text);

            Assert.Equal(value, threshold.Amount);
            Assert.Equal(unit, threshold.ByteUnit);
            Assert.Null(threshold.TimeUnit);
        }

        [Theory]
        [InlineData("1MB/s", 1)]
        [InlineData("10Mbit/s", 10)]
        public void Rates_CarryTheirTimeUnit(string text, long value)
        {
            var threshold = Parse(text);

            Assert.Equal(value, threshold.Amount);
            Assert.Equal(TimeSpan.FromSeconds(1), threshold.TimeUnit);
        }

        [Fact]
        public void Whitespace_IsIgnoredThroughout()
        {
            var threshold = Parse(" 10 Mbit / s ");

            Assert.Equal(10, threshold.Amount);
            Assert.Equal((1L << 20) / 8, threshold.ByteUnit);
            Assert.Equal(TimeSpan.FromSeconds(1), threshold.TimeUnit);
        }

        [Theory]
        [InlineData("100bit")]  // less than a byte – refused, not rounded...
        [InlineData("100BIT")]  // ...in every casing
        [InlineData("10Xb")]
        [InlineData("MB")]
        [InlineData("10MB/y")]
        [InlineData("ten")]
        public void Garbage_IsRefusedLoudly(string text)
        {
            Assert.Throws<FormatException>(() => Parse(text));
        }
    }
}
