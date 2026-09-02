using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// The byte-counter thresholds: minIO and minTraffic delta the same kind of monotonic counter
    /// the CPU threshold does, chain with it by 'and', and — where a platform cannot answer at
    /// all — fail open rather than report a working group idle.
    /// </summary>
    public class ProcessWatchIOTests
    {
        private static readonly TransmissionThreshold OneMegabyte = new() { Amount = 1, ByteUnit = 1L << 20 };

        private static ProcessWatchInfo Info(TransmissionThreshold? minIO = null, TransmissionThreshold? minTraffic = null, ProcessingThreshold? minCPU = null)
        {
            return new ProcessWatchInfo("chrome") { Name = "Browser", MinIO = minIO, MinTraffic = minTraffic, MinCPU = minCPU };
        }

        private static ProcessWatch Watch(ProcessWatchInfo info, params IProcess[] processes)
        {
            return ProcessWatchTests.Watch(info, new FakeProcessSource(processes));
        }

        // the unit-less refusal moved to the resolve pipeline with the rest of the threshold
        // questions – see ProcessMetricValidationTests.UnitlessThreshold_FailsTheResolve

        [Fact]
        public void WithoutThreshold_IOIsNeverSampled()
        {
            var chrome = new FakeProcess(101, "chrome") { Cpu = TimeSpan.Zero, Disk = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minCPU: new ProcessingThreshold(TimeSpan.FromMilliseconds(10))), chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            Assert.Equal(0, chrome.DiskSamples); // only the configured attribute costs a syscall
        }

        [Fact]
        public void WithThreshold_TransfersAboveItAreDemand()
        {
            var chrome = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minIO: OneMegabyte), chrome);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));         // baseline cycle

            chrome.Disk = new ProcessInputOutput(3L << 20, 1L << 20);              // 4 MiB moved since

            var token = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            Assert.Equal(4L << 20, token.Metrics().Storage?.Bytes);
        }

        [Fact]
        public void WithThreshold_TransfersBelowItAreIdle()
        {
            var chrome = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minIO: OneMegabyte), chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            chrome.Disk = new ProcessInputOutput(1024, 1024); // a trickle, not a transfer

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void CountersSteppingBackwards_DentOnlyTheirOwnField()
        {
            // the macOS write ledger is debited when dirtied pages are invalidated; a negative
            // delta poisoning the sum would report a busy group idle
            var writer = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 10L << 20) };
            var reader = new FakeProcess(102, "chrome") { Disk = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minIO: OneMegabyte), writer, reader);

            watch.Inspect(TimeSpan.FromSeconds(2));

            writer.Disk = new ProcessInputOutput(0, 5L << 20);  // the ledger shrank by 5 MiB
            reader.Disk = new ProcessInputOutput(2L << 20, 0);  // while the reader did honest work

            var token = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            Assert.Equal(2L << 20, token.Metrics().Storage?.Bytes);
        }

        [Fact]
        public void ProcessJoiningTheGroup_DoesNotCountItsPastAsTransfer()
        {
            var chrome = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 0) };

            var source = new FakeProcessSource(chrome);
            var watch = ProcessWatchTests.Watch(Info(minIO: OneMegabyte), source);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // adopted with gigabytes behind it – bytes that were never this interval's to count
            source.Start(new FakeProcess(102, "chrome") { Disk = new ProcessInputOutput(1L << 30, 1L << 30) });

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ThresholdsChainWithAnd()
        {
            var chrome = new FakeProcess(101, "chrome") { Cpu = TimeSpan.Zero, Disk = new ProcessInputOutput(0, 0) };

            var info = Info(minIO: OneMegabyte, minCPU: new ProcessingThreshold(TimeSpan.FromMilliseconds(10)));

            var watch = Watch(info, chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            chrome.Cpu = TimeSpan.FromSeconds(1); // busy, but not transferring

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));

            chrome.Cpu = TimeSpan.FromSeconds(2);            // busy...
            chrome.Disk = new ProcessInputOutput(8L << 20, 0);        // ...and transferring

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.NotNull(usage.Processor);  // both measurements ride the one token
            Assert.NotNull(usage.Storage);
        }

        [Fact]
        public void TrafficThreshold_TransfersAboveItAreDemand()
        {
            // the traffic path must ride its own counters and its own token field – a watch that
            // cross-wired it to the storage metric would fail here twice over
            var chrome = new FakeProcess(101, "chrome") { Net = new ProcessInputOutput(0, 0), Disk = null };

            var watch = Watch(Info(minTraffic: OneMegabyte), chrome);

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));       // baseline cycle

            chrome.Net = new ProcessInputOutput(3L << 20, 1L << 20);             // 4 MiB transferred since

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.Equal(4L << 20, usage.Traffic?.Bytes);
            Assert.Null(usage.Storage);                                   // nothing measured storage
        }

        [Fact]
        public void CountersSteppingBackwards_AreClampedPerField_NotPerProcess()
        {
            // one process, one field falling while the other rises: clamped per field the rise
            // survives; clamped on the process' summed delta it would vanish – the exact mistake
            // a ready-made total would invite
            var chrome = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 5L << 20) };

            var watch = Watch(Info(minIO: OneMegabyte), chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            chrome.Disk = new ProcessInputOutput(2L << 20, 0); // reads +2 MiB, the write ledger debited -5 MiB

            var token = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            Assert.Equal(2L << 20, token.Metrics().Storage?.Bytes);
        }

        [Fact]
        public void PartiallyAnsweredThreshold_DoesNotFailOpen()
        {
            // fail-open is for "nobody can answer"; one process answering makes the measurement
            // real, and a real measurement below the threshold is idle
            var mute = new FakeProcess(101, "chrome") { Disk = null };
            var quiet = new FakeProcess(102, "chrome") { Disk = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minIO: OneMegabyte), mute, quiet);

            watch.Inspect(TimeSpan.FromSeconds(2));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void EmptyRoster_YieldsNothingDespiteFailOpen()
        {
            // an empty group must not ride the fail-open path into a phantom demand token
            var watch = Watch(Info(minTraffic: OneMegabyte));

            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void UnansweredThreshold_FailsOpen()
        {
            // a platform with no meter answers null for every process; failing closed would let
            // that measurement gap report the group idle – and onIdle can be 'stop'
            var chrome = new FakeProcess(101, "chrome") { Net = null };

            var watch = Watch(Info(minTraffic: OneMegabyte), chrome);

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void UnansweredThreshold_StillVetoedByTheOthers()
        {
            var chrome = new FakeProcess(101, "chrome") { Cpu = TimeSpan.Zero, Net = null };

            var info = Info(minTraffic: OneMegabyte, minCPU: new ProcessingThreshold(TimeSpan.FromMilliseconds(10)));

            var watch = Watch(info, chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            // the unmeasurable attribute degrades the watch to what the others still measure
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void RateThreshold_TokensCarryAmountsAndDisplayFormats()
        {
            // Both sides retain interval bytes; their format records whether ToString should
            // present that amount directly or derive a per-second rate from the sample duration.
            var rate = new TransmissionThreshold { Amount = 1, ByteUnit = 1L << 20, TimeUnit = TimeSpan.FromSeconds(1) };

            var chrome = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 0), Net = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minIO: rate, minTraffic: OneMegabyte), chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            chrome.Disk = new ProcessInputOutput(10L << 30, 0);
            chrome.Net = new ProcessInputOutput(4L << 20, 0);

            var usage = Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2))).Metrics();

            Assert.Equal(TransferMetricFormat.BytesPerSecond, usage.Storage?.Format);
            Assert.True(usage.Storage?.Bytes > 0);

            Assert.Equal(4L << 20, usage.Traffic?.Bytes);
            Assert.Equal(TransferMetricFormat.Bytes, usage.Traffic?.Format);
        }

        [Fact]
        public void RateThreshold_ComparesAverages()
        {
            // 1MB/s over a poll interval: a burst far above the rate must register...
            var rate = new TransmissionThreshold { Amount = 1, ByteUnit = 1L << 20, TimeUnit = TimeSpan.FromSeconds(1) };

            var chrome = new FakeProcess(101, "chrome") { Disk = new ProcessInputOutput(0, 0) };

            var watch = Watch(Info(minIO: rate), chrome);

            watch.Inspect(TimeSpan.FromSeconds(2));

            chrome.Disk = new ProcessInputOutput(10L << 30, 0); // 10 GiB since – above 1MB/s at any realistic interval

            Assert.Single(watch.Inspect(TimeSpan.FromSeconds(2)));

            // ...and no transfer at all must not
            Assert.Empty(watch.Inspect(TimeSpan.FromSeconds(2)));
        }
    }
}
