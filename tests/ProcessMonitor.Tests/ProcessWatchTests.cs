using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using MadWizard.Desomnia.Processes.Watch;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// What a watch asks of its processes on every inspection cycle. Without a <c>minCPU</c>
    /// threshold the answer is the roster alone: sampling processor time costs a syscall per
    /// watched process, every cycle, for a number the watch would then throw away.
    /// </summary>
    public class ProcessWatchTests
    {
        private sealed class MetricsProcessWatch(ProcessWatchMetrics metrics)
            : ProcessWatch(metrics, "Metrics")
        {
            protected override bool ShouldWatchProcess(IProcess process) => true;
        }

        internal static ProcessWatch Watch(ProcessWatchInfo info, IProcessManager manager)
        {
            return new PatternProcessWatch(info) { Manager = manager };
        }

        internal static ProcessWatch Watch(ProcessWatchMetrics metrics, IProcessManager manager)
        {
            return new MetricsProcessWatch(metrics) { Manager = manager };
        }

        private static ProcessWatch Watch(ProcessWatchInfo info, params IProcess[] processes)
        {
            return Watch(info, new FakeProcessSource(processes));
        }

        [Fact]
        public void WithoutThreshold_ProcessorTimeIsNeverSampled()
        {
            var chrome = new FakeProcess(101, "chrome"); // counts every sample taken of it

            var watch = Watch(new ProcessWatchInfo("chrome") { Name = "Browser" }, chrome);

            var tokens = watch.Inspect(TimeSpan.FromSeconds(2));

            Assert.Single(tokens);
            Assert.Equal(0, chrome.CpuSamples);
        }

        /// <summary>
        /// onStart and onStop mark the group's *transitions*, not its individual members: they fire
        /// when the first process appears and when the last one goes, and at no other time. That
        /// decision has to be atomic with the roster change that prompts it, which is the whole
        /// reason the roster is behind a lock rather than a concurrent collection.
        /// </summary>
        [Fact]
        public void GroupEvents_MarkOnlyTheFirstArrivalAndTheLastDeparture()
        {
            var source = new FakeProcessSource();

            var watch = Watch(new ProcessWatchInfo("chrome") { Name = "Browser" }, source);

            var started = 0;
            var stopped = 0;

            watch.Started += _ => { started++; return Task.CompletedTask; };
            watch.Stopped += _ => { stopped++; return Task.CompletedTask; };

            var window = new FakeProcess(101, "chrome");
            var tab = new FakeProcess(102, "chrome");

            source.Start(window);
            source.Start(tab); // the group was already up; nothing to announce

            Assert.Equal(1, started);
            Assert.Equal(0, stopped);

            source.Stop(window); // one left, so the group is still up

            Assert.Equal(0, stopped);

            source.Stop(tab);

            Assert.Equal(1, stopped);
            Assert.Equal(1, started);
        }

        [Fact]
        public void SameProcessArrivingTwice_LeavesInOneStep()
        {
            var source = new FakeProcessSource();

            var watch = Watch(new ProcessWatchInfo("chrome") { Name = "Browser" }, source);

            source.Start(new FakeProcess(101, "chrome"));
            source.Start(new FakeProcess(101, "chrome")); // the same pid arriving under a second object

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            source.Stop(new FakeProcess(101, "chrome"));

            // held by pid, so one exit empties the group; held by object, the duplicate would linger
            // and keep asserting demand for a process that is no longer there
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void WithoutThreshold_EmptyRosterReportsNoUsage()
        {
            var watch = Watch(new ProcessWatchInfo("chrome") { Name = "Browser" });

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void WithThreshold_ProcessorTimeIsSampled()
        {
            var chrome = new FakeProcess(101, "chrome") { Cpu = TimeSpan.Zero };

            var info = new ProcessWatchInfo("chrome") { Name = "Browser", MinCPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)) };

            var watch = Watch(info, chrome);

            Assert.Equal(1, chrome.CpuSamples);             // baseline captured on introduction

            watch.Inspect(TimeSpan.FromSeconds(2));       // establishes the baseline
            chrome.Cpu = TimeSpan.FromMilliseconds(500);  // half a second of work since
            var tokens = watch.Inspect(TimeSpan.FromSeconds(2));

            var usage = Assert.Single(tokens).Metrics();
            var expectedCapacity = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                ? TimeSpan.FromSeconds(2) * Environment.ProcessorCount
                : TimeSpan.FromSeconds(2);

            Assert.Equal(TimeSpan.FromSeconds(2), usage.SampleDuration);
            Assert.True(usage.Processor?.Time >= TimeSpan.FromMilliseconds(500));
            Assert.Equal(expectedCapacity, usage.Processor?.TimeReference);
            Assert.Equal(ProcessingMetricFormat.Time, usage.Processor?.Format);
            Assert.Equal(3, chrome.CpuSamples);
        }

        [Fact]
        public void EveryInterval_IsNormalizedFromItsActualDuration()
        {
            var source = new FakeProcessSource();
            var metrics = new ProcessWatchMetrics
            {
                MinCPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)),
                MinGPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)),
                MinIO = new TransmissionThreshold { Amount = 2, ByteUnit = 1L << 20 },
                MinTraffic = new TransmissionThreshold { Amount = 2, ByteUnit = 1L << 20 },
            };
            var watch = new MetricsProcessWatch(metrics) { Manager = source };

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(10)));

            var process = new FakeProcess(101, "game")
            {
                Cpu = TimeSpan.Zero,
                Gpu = TimeSpan.Zero,
                Disk = new ProcessInputOutput(0, 0),
                Net = new ProcessInputOutput(0, 0),
            };

            source.Start(process);

            Assert.Equal(1, process.CpuSamples);
            Assert.Equal(1, process.GpuSamples);
            Assert.Equal(1, process.DiskSamples);

            Thread.Sleep(25);
            process.Cpu = TimeSpan.FromMilliseconds(1);
            process.Gpu = TimeSpan.FromMilliseconds(1);
            process.Disk = new ProcessInputOutput(1L << 20, 0);
            process.Net = new ProcessInputOutput(1L << 20, 0);

            var first = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(10))).Metrics();

            Assert.True(first.Processor?.Time > process.Cpu);
            Assert.True(first.GraphicsProcessor?.Time > process.Gpu);
            Assert.True(first.Storage?.Bytes > process.Disk?.BytesIn);
            Assert.True(first.Traffic?.Bytes > process.Net?.BytesIn);

            Thread.Sleep(25);
            process.Cpu += TimeSpan.FromMilliseconds(1);
            process.Gpu += TimeSpan.FromMilliseconds(1);
            process.Disk = new ProcessInputOutput(2L << 20, 0);
            process.Net = new ProcessInputOutput(2L << 20, 0);

            // The second interval must be normalized too; its raw deltas satisfy none of the minima.
            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void WithThreshold_ProcessLeavingTheGroupDoesNotMakeItLookIdle()
        {
            var window = new FakeProcess(101, "chrome") { Cpu = TimeSpan.FromMinutes(1) };
            var tab = new FakeProcess(102, "chrome") { Cpu = TimeSpan.FromMinutes(1) };

            var info = new ProcessWatchInfo("chrome") { Name = "Browser", MinCPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)) };

            var source = new FakeProcessSource(window, tab);
            var watch = Watch(info, source);

            watch.Inspect(TimeSpan.FromSeconds(2)); // establishes a baseline per process

            source.Stop(tab);                        // a tab closes, taking a minute of history with it
            window.Cpu = TimeSpan.FromSeconds(61);   // while the window keeps working

            // measured as one group total, the departure would read as -59s of work and report the
            // whole browser idle for this cycle
            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void WithThreshold_ProcessJoiningTheGroupDoesNotCountItsPastAsWork()
        {
            var window = new FakeProcess(101, "chrome") { Cpu = TimeSpan.Zero };

            var info = new ProcessWatchInfo("chrome") { Name = "Browser", MinCPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)) };

            var source = new FakeProcessSource(window);
            var watch = Watch(info, source);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // adopted with an hour behind it – time that was never this interval's to count
            source.Start(new FakeProcess(102, "chrome") { Cpu = TimeSpan.FromHours(1) });

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void WithThreshold_ProcessDyingMidCycleDoesNotAbortTheInspection()
        {
            var chrome = new FakeProcess(101, "chrome") { Cpu = TimeSpan.FromMilliseconds(500) };
            var gone = new FakeProcess(102, "chrome") { Cpu = null }; // died between the poll and the cycle

            var info = new ProcessWatchInfo("chrome") { Name = "Browser", MinCPU = new ProcessingThreshold(TimeSpan.FromMilliseconds(10)) };

            var watch = Watch(info, chrome, gone);

            watch.Inspect(TimeSpan.FromSeconds(2));
            chrome.Cpu = TimeSpan.FromSeconds(1);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))); // the survivor still reports demand
        }
    }
}
