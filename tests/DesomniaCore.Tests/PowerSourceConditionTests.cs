using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Environments;
using MadWizard.Desomnia.Power.Source;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class PowerSourceConditionTests
    {
        private sealed class FakeProbe : IPowerSource
        {
            public PowerSource Source { get; set; }

            public event EventHandler? SourceChanged { add { } remove { } }
        }

        [Theory]
        [InlineData("ac", PowerSource.AC, true)]
        [InlineData("AC", PowerSource.AC, true)]
        [InlineData("ac", PowerSource.Battery, false)]
        [InlineData("battery", PowerSource.Battery, true)]
        [InlineData("battery", PowerSource.AC, false)]
        public void MatchesTheRequiredPowerSource(string value, PowerSource current, bool expected)
        {
            var condition = new PowerSourceCondition(value) { Logger = NullLogger.Instance, Power = new FakeProbe { Source = current } };

            Assert.Equal(expected, condition.IsSatisfied());
        }

        [Theory]
        [InlineData("ac")]
        [InlineData("battery")]
        public void UnknownPowerSource_NeverMatches(string value)
        {
            var condition = new PowerSourceCondition(value) { Logger = NullLogger.Instance, Power = new FakeProbe { Source = PowerSource.Unknown } };

            Assert.False(condition.IsSatisfied());
        }

        [Fact]
        public void InvalidValue_Throws()
        {
            Assert.Throws<ConfigurationValueException>(() => new PowerSourceCondition("solar") { Logger = NullLogger.Instance, Power = new FakeProbe() });
        }
    }
}
