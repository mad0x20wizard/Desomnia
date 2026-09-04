using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// The graphics threshold: minGPU deltas the graphics clock exactly like minCPU deltas the
    /// processor's and reports an error when no matching process can supply the configured clock.
    /// </summary>
    public class ProcessWatchGPUTests
    {
        private static readonly ProcessingThreshold TenMilliseconds = new(TimeSpan.FromMilliseconds(10));

        private static ProcessWatchInfo Info(ProcessingThreshold? minGPU = null, ProcessingThreshold? minCPU = null, TransmissionThreshold? minIO = null)
        {
            return new ProcessWatchInfo("game") { Name = "Game", MinGPU = minGPU, MinCPU = minCPU, MinIO = minIO };
        }

        private static ProcessWatch Watch(ProcessWatchInfo info, params IProcess[] processes)
        {
            return ProcessWatchTests.Watch(info, new FakeProcessSource(processes));
        }

        private static ProcessWatch Watch(ProcessWatchInfo info, ProcessMetric shared, params IProcess[] processes)
        {
            return ProcessWatchTests.Watch(info, new FakeProcessSource(processes), shared);
        }

        [Fact]
        public void WithoutThreshold_GraphicsIsNeverSampled()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minCPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            Assert.Equal(0, game.GpuSamples); // only the configured attribute costs a syscall
        }

        [Fact]
        public void WithThreshold_UsageAboveItIsDemand()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: new ProcessingThreshold(0.1)), game);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));  // baseline cycle

            game.Gpu = TimeSpan.FromMinutes(10); // far beyond any share of the interval

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.Equal(TimeSpan.FromSeconds(2), usage.SampleDuration);
            Assert.Equal(TimeSpan.FromSeconds(2), usage.GraphicsProcessor?.TimeReference);
            Assert.Equal(ProcessingMetricFormat.Percentage, usage.GraphicsProcessor?.Format);
        }

        [Fact]
        public void WithThreshold_UsageBelowItIsIdle()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.FromMinutes(10) };

            var watch = Watch(Info(minGPU: new ProcessingThreshold(0.1)), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // the clock stands still – ten idle minutes of history are not this interval's work
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void AbsoluteThreshold_ComparesTheInterval()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(500);

            var token = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            Assert.True(token.Metrics().GraphicsProcessor?.Time >= TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ZeroThreshold_AlwaysMatchesTheMeasuredMetric()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: new ProcessingThreshold(TimeSpan.Zero)), game);

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.Equal(TimeSpan.Zero, usage.GraphicsProcessor?.Time);
            Assert.True(usage["GPU"]);
            Assert.Equal(2, game.GpuSamples);
        }

        [Fact]
        public void ThresholdsChainWithAnd()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero, Disk = new ProcessInputOutput(0, 0) };

            var info = Info(minGPU: TenMilliseconds, minIO: new TransmissionThreshold { Amount = 1, ByteUnit = 1L << 20 });

            var watch = Watch(info, game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(500); // rendering, but not transferring

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));

            game.Gpu = TimeSpan.FromSeconds(1);                 // rendering...
            game.Disk = new ProcessInputOutput(8L << 20, 0);    // ...and transferring

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.NotNull(usage.GraphicsProcessor); // both measurements ride the one token
            Assert.NotNull(usage.Storage);
        }

        [Fact]
        public void UnansweredThreshold_IsAnError()
        {
            // a platform without a graphics clock answers null for every process; failing closed
            // would let that gap report the group idle – and onIdle can be 'stop'
            var game = new FakeProcess(101, "game") { Gpu = null };

            var watch = Watch(Info(minGPU: TenMilliseconds), game);

            Assert.Throws<InvalidOperationException>(() => watch.Inspect(TimeSpan.FromSeconds(2)).ToArray());
        }

        [Fact]
        public void UnansweredThreshold_IsAnErrorEvenWhenOtherMetricsAnswer()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = null };

            var watch = Watch(Info(minGPU: TenMilliseconds, minCPU: TenMilliseconds), game);

            Assert.Throws<InvalidOperationException>(() => watch.Inspect(TimeSpan.FromSeconds(2)).ToArray());
        }

        [Fact]
        public void PartiallyAnsweredThreshold_DoesNotFailOpen()
        {
            // One process answering is enough to make the group measurement available.
            // real, and a real measurement below the threshold is idle
            var mute = new FakeProcess(101, "game") { Gpu = null };
            var quiet = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: TenMilliseconds), mute, quiet);

            watch.Inspect(TimeSpan.FromSeconds(2));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ClockSteppingBackwards_IsClampedNotNegative()
        {
            // a driver restart resets the scheduler's running time; the survivor's honest work
            // must not be swallowed by another process' clock leaping backwards
            var reset = new FakeProcess(101, "game") { Gpu = TimeSpan.FromSeconds(10) };
            var busy = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: TenMilliseconds), reset, busy);

            watch.Inspect(TimeSpan.FromSeconds(2));

            reset.Gpu = TimeSpan.FromSeconds(2);            // the clock started over
            busy.Gpu = TimeSpan.FromMilliseconds(20);       // while this one kept working

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ClockSteppingBackwards_EstablishesANewBaseline()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.FromSeconds(10) };
            var watch = Watch(Info(minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.Zero;
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));

            game.Gpu = TimeSpan.FromMilliseconds(20);
            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ProcessJoiningTheGroup_DoesNotCountItsPastAsWork()
        {
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };

            var source = new FakeProcessSource(game);
            var watch = ProcessWatchTests.Watch(Info(minGPU: TenMilliseconds), source);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // adopted with an hour of rendering behind it – time that was never this interval's
            source.Start(new FakeProcess(102, "game") { Gpu = TimeSpan.FromHours(1) });

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void EqualGraphicsValues_CountOnce()
        {
            var app = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };
            var helper = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };
            var info = Info(minGPU: new ProcessingThreshold(TimeSpan.FromMilliseconds(12)));

            var watch = Watch(info, ProcessMetric.Graphics, app, helper);

            watch.Inspect(TimeSpan.FromSeconds(2));

            app.Gpu = helper.Gpu = TimeSpan.FromMilliseconds(8);

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            Thread.Sleep(20);

            // One normalized 8ms clock is below 12ms; counting the duplicate would exceed it.
            Assert.Empty(watch.Inspect(System.Diagnostics.Stopwatch.GetElapsedTime(started)));
            Assert.Equal(3, app.GpuSamples);
            Assert.Equal(3, helper.GpuSamples);
        }

        [Fact]
        public void RemovingAProcessWithTheSameValue_KeepsTheOtherBaseline()
        {
            var app = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };
            var helper = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };
            var source = new FakeProcessSource(app, helper);
            var watch = ProcessWatchTests.Watch(Info(minGPU: TenMilliseconds), source);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));

            source.Stop(app);
            helper.Gpu = TimeSpan.FromMilliseconds(500);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ProcessesWithTheirOwnClocks_StillAddUp()
        {
            // Different values represent distinct GPU clocks and are both included.
            var one = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };
            var two = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: TenMilliseconds), one, two);

            watch.Inspect(TimeSpan.FromSeconds(2));

            one.Gpu = TimeSpan.FromMilliseconds(8);
            two.Gpu = TimeSpan.FromMilliseconds(9);

            var token = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            Assert.True(token.Metrics().GraphicsProcessor?.Time >= TimeSpan.FromMilliseconds(17));
            Assert.Equal(3, one.GpuSamples);
            Assert.Equal(3, two.GpuSamples);
        }

        [Fact]
        public void EmptyRoster_YieldsNothingDespiteFailOpen()
        {
            // A user-facing process resource still requires at least one matching process.
            var watch = Watch(Info(minGPU: TenMilliseconds));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }
    }
}
