using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Configuration.Binding;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MadWizard.Desomnia.Tests
{
    public class WatchExpressionTests
    {
        private sealed record TestWatchMetrics : WatchMetrics
        {
            public TestWatchMetrics()
            {
                Watch = WatchExpression.DefaultAND;
            }
        }

        private static MetricsUsage Metrics(params (string Name, bool Value)[] metrics)
        {
            var values = new MetricsUsage();
            foreach (var (name, value) in metrics)
                values.Add(name, value);
            return values;
        }

        [Fact]
        public void OperatorsUseStandardPrecedenceAndIgnoreCase()
        {
            var expression = new WatchExpression("cpu OR gpu aNd Input");

            Assert.True(expression.Evaluate(Metrics(("CPU", true), ("GPU", false), ("input", false))));
            Assert.False(expression.Evaluate(Metrics(("CPU", false), ("GPU", true), ("Input", false))));
        }

        [Fact]
        public void ParenthesesOverridePrecedence()
        {
            var expression = new WatchExpression("(CPU or GPU) and Input");

            Assert.False(expression.Evaluate(Metrics(("CPU", true), ("GPU", false), ("Input", false))));
        }

        [Fact]
        public void LiteralsCanFormTheWholeExpression()
        {
            Assert.True(new WatchExpression("true").Evaluate(Metrics()));
            Assert.False(new WatchExpression("FALSE").Evaluate(Metrics(("CPU", true))));
        }

        [Fact]
        public void EveryBranchIsEvaluatedAndUnknownMetricsAreErrors()
        {
            var expression = new WatchExpression("false and Missing");

            Assert.Throws<InvalidOperationException>(() => expression.Evaluate(Metrics()));
        }

        [Theory]
        [InlineData("AND")]
        [InlineData("OR")]
        public void CatchAllExpressionsDemandForAnEmptyMetricSet(string text)
        {
            Assert.True(new WatchExpression(text).Evaluate(Metrics()));
        }

        [Fact]
        public void RelativeMergeGroupsEachFormulaAsOneOperand()
        {
            var previous = new WatchExpression("CPU or Input");
            var next = new WatchExpression("and StreamTraffic or GPU");
            var merged = previous << next;

            Assert.False(merged.Evaluate(Metrics(
                ("CPU", false), ("Input", false), ("StreamTraffic", false), ("GPU", true))));
        }

        [Fact]
        public void NonDefaultExpressionReplacesDefaultEvenWhenRelative()
        {
            var merged = WatchExpression.DefaultOR << new WatchExpression("and CPU");

            Assert.False(merged.IsDefault);
            Assert.False(merged.MergeOperator is not null);
            Assert.False(merged.Evaluate(Metrics(("CPU", false), ("GPU", true))));
        }

        [Fact]
        public void DefaultExpressionNeverChangesANonDefaultExpression()
        {
            var previous = new WatchExpression("CPU");
            var merged = previous << new WatchExpression("default or GPU");

            Assert.Equal(previous, merged);
        }

        [Fact]
        public void YieldCanReplaceAnExpressionButIsTerminal()
        {
            var yielded = WatchExpression.DefaultAND << WatchExpression.Yield;

            Assert.True(yielded.IsYield);
            Assert.Throws<InvalidOperationException>(() => yielded << new WatchExpression("CPU"));
            Assert.Throws<InvalidOperationException>(() => yielded.Evaluate(Metrics()));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("default")]
        [InlineData("CPU and")]
        [InlineData("CPU default GPU")]
        public void InvalidSyntaxIsRejected(string text)
        {
            Assert.Throws<FormatException>(() => new WatchExpression(text));
        }

        [Fact]
        public void MetricValuesRejectCaseInsensitiveDuplicates()
        {
            var values = Metrics(("CPU", true));

            Assert.Throws<InvalidOperationException>(() => values.Add("cpu", false));
        }

        [Fact]
        public void StrictConfigurationBindingUsesTheExpressionConverter()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["watch"] = "(CPU or GPU) and true" })
                .Build();

            var result = StrictConfigurationBinder.Get<TestWatchMetrics>(configuration)!;

            Assert.True(result.Watch.Evaluate(Metrics(("CPU", false), ("GPU", true))));
        }

        [Fact]
        public void StrictConfigurationBindingRejectsAnEmptyWatchAttribute()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["watch"] = "" })
                .Build();

            Assert.Throws<ConfigurationValueException>(() => StrictConfigurationBinder.Get<TestWatchMetrics>(configuration));
        }
    }
}
