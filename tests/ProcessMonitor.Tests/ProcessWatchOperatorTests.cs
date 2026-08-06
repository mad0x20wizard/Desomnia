using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// How the min attributes combine. The default is 'and' – every configured threshold has to
    /// hold at once – which is what a group doing one kind of work wants. A group whose work moves
    /// between the metrics needs 'or': a game renders without computing much, a build computes
    /// without rendering at all, and under 'and' either reads as idle in the middle of the work.
    /// </summary>
    public class ProcessWatchOperatorTests
    {
        private static readonly ProcessingThreshold TenMilliseconds = new(TimeSpan.FromMilliseconds(10));

        private static readonly TransmissionThreshold OneMegabyte = new() { Amount = 1, ByteUnit = 1L << 20 };

        private static ProcessWatchInfo Info(ProcessWatchMetrics.Operator min, ProcessingThreshold? minCPU = null,
            ProcessingThreshold? minGPU = null, TransmissionThreshold? minTraffic = null)
        {
            return new ProcessWatchInfo("game") { Name = "Game", Min = min, MinCPU = minCPU, MinGPU = minGPU, MinTraffic = minTraffic };
        }

        private static ProcessWatch Watch(ProcessWatchInfo info, params IProcess[] processes)
        {
            return ProcessWatchTests.Watch(info, new FakeProcessSource(processes));
        }

        [Fact]
        public void TheDefault_IsAnd()
        {
            // an existing configuration keeps the reading it always had
            Assert.Equal(ProcessWatchMetrics.Operator.AND, new ProcessWatchInfo("game") { Name = "Game" }.Min);
        }

        [Fact]
        public void Or_OneThresholdCarriesTheGroup()
        {
            // the case the operator exists for: rendering hard, computing almost nothing
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(500); // the GPU alone is over its threshold

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            // the token still reports both, so the log says what the group did and not merely
            // which threshold happened to carry it
            Assert.Equal(TimeSpan.FromMilliseconds(500), usage.GraphicsProcessingTime);
            Assert.Equal(TimeSpan.Zero, usage.ProcessingTime);
        }

        [Fact]
        public void And_OneThresholdIsNotEnough()
        {
            // the same measurement under the default operator is idle – the contrast that makes
            // the flag worth having
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(ProcessWatchMetrics.Operator.AND, minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(500);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_NoThresholdReached_IsIdle()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(5); // both below their threshold
            game.Gpu = TimeSpan.FromMilliseconds(5);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_EveryThresholdReached_IsStillOneDemand()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(500);
            game.Gpu = TimeSpan.FromMilliseconds(500);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        /// <summary>
        /// An attribute nothing could measure contributes the operator's identity, so it is left
        /// out of the tally entirely: under 'or' it must not carry a group nobody found busy, just
        /// as under 'and' it must not veto one the others did.
        /// </summary>
        [Fact]
        public void Or_UnmeasurableThreshold_DoesNotCarryTheGroupOnItsOwn()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = null }; // no graphics clock here

            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(5); // measurable, and below its threshold

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_UnmeasurableThreshold_LeavesTheOthersToCarryIt()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = null };

            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minCPU: TenMilliseconds, minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Cpu = TimeSpan.FromMilliseconds(500);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        /// <summary>
        /// With nothing measurable at all there is no tally to believe either way, and the identity
        /// alone would answer on the operator: 'and' would assume demand and 'or' would report the
        /// group idle. Both fail open instead, because onIdle can be 'stop' – a missing counter
        /// must not be what kills a working process.
        /// </summary>
        [Fact]
        public void Or_NothingMeasurableAtAll_StillFailsOpen()
        {
            var game = new FakeProcess(101, "game") { Gpu = null, Net = null };

            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minGPU: TenMilliseconds, minTraffic: OneMegabyte), game);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_EmptyRoster_YieldsNothing()
        {
            // 'or' starts unsatisfied, but an empty group must not reach the fail-open path either
            var watch = Watch(Info(ProcessWatchMetrics.Operator.OR, minGPU: TenMilliseconds, minCPU: TenMilliseconds));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void Or_ASingleThreshold_ReadsTheSameAsAnd()
        {
            // with one attribute configured the operator has nothing to combine, and neither
            // reading may drift from the other
            var busy = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero };
            var quiet = new FakeProcess(102, "game") { Cpu = TimeSpan.Zero };

            var or = Watch(Info(ProcessWatchMetrics.Operator.OR, minCPU: TenMilliseconds), busy);
            var and = Watch(Info(ProcessWatchMetrics.Operator.AND, minCPU: TenMilliseconds), quiet);

            or.Inspect(TimeSpan.FromSeconds(2));
            and.Inspect(TimeSpan.FromSeconds(2));

            busy.Cpu = quiet.Cpu = TimeSpan.FromMilliseconds(500);

            Assert.Single(or.Inspect(TimeSpan.FromSeconds(2)));
            Assert.Single(and.Inspect(TimeSpan.FromSeconds(2)));
        }
    }
}
