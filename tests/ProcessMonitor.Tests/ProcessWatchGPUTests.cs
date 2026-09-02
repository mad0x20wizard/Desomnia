using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// The graphics threshold: minGPU deltas the graphics clock exactly like minCPU deltas the
    /// processor's – same threshold type, same strict comparison – but fails open where a whole
    /// platform cannot answer, the way the byte counters do.
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

            Assert.Equal(TimeSpan.FromMilliseconds(500), token.Metrics().GraphicsProcessor?.Time);
        }

        [Fact]
        public void ExactlyTheThreshold_IsIdle()
        {
            // minGPU mirrors minCPU's strict '>' – reaching the threshold is not crossing it
            var game = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            game.Gpu = TimeSpan.FromMilliseconds(10);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
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
        public void UnansweredThreshold_FailsOpen()
        {
            // a platform without a graphics clock answers null for every process; failing closed
            // would let that gap report the group idle – and onIdle can be 'stop'
            var game = new FakeProcess(101, "game") { Gpu = null };

            var watch = Watch(Info(minGPU: TenMilliseconds), game);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void UnansweredThreshold_StillVetoedByTheOthers()
        {
            var game = new FakeProcess(101, "game") { Cpu = TimeSpan.Zero, Gpu = null };

            var watch = Watch(Info(minGPU: TenMilliseconds, minCPU: TenMilliseconds), game);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // the unmeasurable attribute degrades the watch to what the others still measure
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void PartiallyAnsweredThreshold_DoesNotFailOpen()
        {
            // fail-open is for "nobody can answer"; one process answering makes the measurement
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
        public void ProcessesSharingAClock_CountItOnce()
        {
            // macOS bills graphics time to a coalition, so an app and the helper it spawned read
            // the same counter: summed per process, one interval of work would count twice and a
            // group would cross a threshold it never reached
            const ulong coalition = 22290;

            var app = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero, Scope = coalition };
            var helper = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero, Scope = coalition };

            var watch = Watch(Info(minGPU: TenMilliseconds), app, helper);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // the one shared ledger advanced by 8ms, which is below the threshold – doubled it
            // would be 16ms and would read as demand
            app.Gpu = helper.Gpu = TimeSpan.FromMilliseconds(8);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ProcessesWithTheirOwnClocks_StillAddUp()
        {
            // the counterpart: where a platform accounts per process, every process is its own
            // scope and the group total is the sum it always was
            var one = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };
            var two = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };

            var watch = Watch(Info(minGPU: TenMilliseconds), one, two);

            watch.Inspect(TimeSpan.FromSeconds(2));

            one.Gpu = two.Gpu = TimeSpan.FromMilliseconds(8); // 16ms between them

            var token = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            Assert.Equal(TimeSpan.FromMilliseconds(16), token.Metrics().GraphicsProcessor?.Time);
        }

        [Fact]
        public void EmptyRoster_YieldsNothingDespiteFailOpen()
        {
            // an empty group must not ride the fail-open path into a phantom demand token
            var watch = Watch(Info(minGPU: TenMilliseconds));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }
    }
}
