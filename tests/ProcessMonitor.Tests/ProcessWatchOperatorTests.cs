using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// How watch expressions combine metric results. The default is 'and' – every configured threshold has to
    /// hold at once – which is what a group doing one kind of work wants. A group whose work moves
    /// between the metrics needs 'or': a game renders without computing much, a build computes
    /// without rendering at all, and under 'and' either reads as idle in the middle of the work.
    /// </summary>
    public class ProcessWatchExpressionTests
    {
        private static readonly ProcessingThreshold TenMilliseconds = new(TimeSpan.FromMilliseconds(10));
        private static readonly ProcessingThreshold TenPercent = new(0.1);

        private static readonly TransmissionThreshold OneMegabyte = new() { Amount = 1, ByteUnit = 1L << 20 };
        private static readonly TransmissionThreshold OneMegabytePerSecond = new()
        {
            Amount = 1,
            ByteUnit = 1L << 20,
            TimeUnit = TimeSpan.FromSeconds(1),
        };

        private static ProcessWatchInfo Info(WatchExpression watch, ProcessingThreshold? minCPU = null,
            ProcessingThreshold? minGPU = null, TransmissionThreshold? minIO = null, TransmissionThreshold? minTraffic = null)
        {
            return new ProcessWatchInfo("game")
            {
                Name = "Game",
                Watch = watch,
                MinCPU = minCPU,
                MinGPU = minGPU,
                MinIO = minIO,
                MinTraffic = minTraffic,
            };
        }

        private static ProcessWatch Watch(ProcessWatchInfo info, params IProcess[] processes)
        {
            return ProcessWatchTests.Watch(info, new FakeProcessSource(processes));
        }

        [Fact]
        public void TheDefault_IsAnd()
        {
            // an existing configuration keeps the reading it always had
            Assert.Equal(WatchExpression.DefaultAND, new ProcessWatchInfo("game") { Name = "Game" }.Watch);
        }

        [Fact]
        public void Or_OneThresholdCarriesTheGroup()
        {
            // the case the operator exists for: rendering hard, computing almost nothing
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(new WatchExpression("OR"), minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(500); // the GPU alone is over its threshold

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.True(usage.GraphicsProcessor?.Time >= TimeSpan.FromMilliseconds(500));
            Assert.Null(usage.Processor);
        }

        [Fact]
        public void Or_YieldedMetricsContainOnlyReachedThresholds()
        {
            var game = new FakeProcess(101, "game")
            {
                Cpu = TimeSpan.Zero,
                Gpu = TimeSpan.Zero,
                Disk = new ProcessInputOutput(0, 0),
                Net = new ProcessInputOutput(0, 0),
            };

            var watch = Watch(Info(new WatchExpression("OR"),
                minCPU: TenPercent,
                minGPU: TenMilliseconds,
                minIO: OneMegabytePerSecond,
                minTraffic: OneMegabyte), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(20);
            game.Net = new ProcessInputOutput(2L << 20, 0);

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.Null(usage.Processor);
            Assert.True(usage.GraphicsProcessor?.Time >= TimeSpan.FromMilliseconds(20));
            Assert.Null(usage.Storage);
            Assert.True(usage.Traffic?.Bytes >= 2L << 20);
        }

        [Fact]
        public void Or_FilteringAlsoAppliesToTheOtherMetricUnits()
        {
            var game = new FakeProcess(101, "game")
            {
                Cpu = TimeSpan.Zero,
                Gpu = TimeSpan.Zero,
                Disk = new ProcessInputOutput(0, 0),
                Net = new ProcessInputOutput(0, 0),
            };

            var watch = Watch(Info(new WatchExpression("OR"),
                minCPU: TenMilliseconds,
                minGPU: TenPercent,
                minIO: OneMegabyte,
                minTraffic: OneMegabytePerSecond), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(20);
            game.Disk = new ProcessInputOutput(0, 0);

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.True(usage.Processor?.Time >= TimeSpan.FromMilliseconds(20));
            Assert.Null(usage.GraphicsProcessor);
            Assert.Null(usage.Storage);
            Assert.Null(usage.Traffic);
        }

        [Fact]
        public void And_OneThresholdIsNotEnough()
        {
            // the same measurement under the default operator is idle – the contrast that makes
            // the flag worth having
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(new WatchExpression("AND"), minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(500);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_NoThresholdReached_IsIdle()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(new WatchExpression("OR"), minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(5); // both below their threshold
            game.Gpu = TimeSpan.FromMilliseconds(5);

            Thread.Sleep(20);
            Assert.Empty(watch.Inspect(TimeSpan.FromMilliseconds(1)));
        }

        [Fact]
        public void Or_EveryThresholdReached_IsStillOneDemand()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(new WatchExpression("OR"), minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(500);
            game.Gpu = TimeSpan.FromMilliseconds(500);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        /// <summary>
        /// An explicitly configured metric has to be readable; it cannot disappear from the
        /// expression merely because the current process did not supply its counter.
        /// </summary>
        [Fact]
        public void Or_UnmeasurableThreshold_IsAnError()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = null }; // no graphics clock here

            var watch = Watch(Info(new WatchExpression("OR"), minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            Assert.Throws<InvalidOperationException>(() => watch.Inspect(TimeSpan.FromSeconds(2)).ToArray());
        }

        [Fact]
        public void Or_UnmeasurableThreshold_IsAnErrorEvenWhenAnotherMetricMatches()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = null };

            var watch = Watch(Info(new WatchExpression("OR"), minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            Assert.Throws<InvalidOperationException>(() => watch.Inspect(TimeSpan.FromSeconds(2)).ToArray());
        }

        /// <summary>
        /// Failure to read every configured metric is an inspection error, independent of the
        /// expression's catch-all identity.
        /// </summary>
        [Fact]
        public void Or_NothingMeasurableAtAll_IsAnError()
        {
            var game = new FakeProcess(101, "game") { Gpu = null, Net = null };

            var watch = Watch(Info(new WatchExpression("OR"), minGPU: TenMilliseconds, minTraffic: OneMegabyte), game);

            Assert.Throws<InvalidOperationException>(() => watch.Inspect(TimeSpan.FromSeconds(2)).ToArray());
        }

        [Fact]
        public void Or_EmptyRoster_YieldsNothing()
        {
            // A user-facing process resource still requires at least one matching process.
            var watch = Watch(Info(new WatchExpression("OR"), minGPU: TenMilliseconds, minCPU: TenMilliseconds));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_ASingleThreshold_ReadsTheSameAsAnd()
        {
            // with one attribute configured the operator has nothing to combine, and neither
            // reading may drift from the other
            var busy = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero };
            var quiet = new FakeProcess(102, "game") { Cpu = TimeSpan.Zero };

            var or = Watch(Info(new WatchExpression("OR"), minCPU: TenMilliseconds), busy);
            var and = Watch(Info(new WatchExpression("AND"), minCPU: TenMilliseconds), quiet);

            or.Inspect(TimeSpan.FromSeconds(2));
            and.Inspect(TimeSpan.FromSeconds(2));

            busy.Cpu = quiet.Cpu = TimeSpan.FromMilliseconds(500);

            Assert.Single(or.Inspect(TimeSpan.FromSeconds(2)));
            Assert.Single(and.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void FormulaCombinesNamedMetricResults()
        {
            var game = new FakeProcess(101, "game")
            {
                Cpu = TimeSpan.Zero,
                Gpu = TimeSpan.Zero,
                Net = new ProcessInputOutput(0, 0),
            };
            var watch = Watch(Info(new WatchExpression("(cpu or GPU) and traffic"),
                minCPU: TenMilliseconds,
                minGPU: TenMilliseconds,
                minTraffic: OneMegabyte), game);

            watch.Inspect(TimeSpan.FromSeconds(2));
            game.Cpu = TimeSpan.FromMilliseconds(500);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));

            game.Cpu = TimeSpan.FromSeconds(1);
            game.Net = new ProcessInputOutput(2L << 20, 0);

            var metrics = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();
            Assert.True(metrics["CPU"]);
            Assert.False(metrics["GPU"]);
            Assert.True(metrics["Traffic"]);
        }

        [Fact]
        public void LiteralFalseDisablesAnExistingProcessResource()
        {
            var game = new FakeProcess(101, "game");
            var watch = Watch(Info(new WatchExpression("false")), game);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ReferencingAnUnconfiguredMetricFailsOnlyDuringEvaluation()
        {
            var game = new FakeProcess(101, "game");
            var watch = Watch(Info(new WatchExpression("CPU")), game);

            Assert.Throws<InvalidOperationException>(() => watch.Inspect(TimeSpan.FromSeconds(2)).ToArray());
        }
    }
}
