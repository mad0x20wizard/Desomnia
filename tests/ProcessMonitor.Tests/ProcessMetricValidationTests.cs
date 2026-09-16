using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Middleware;
using MadWizard.Desomnia.Processes.Watch;
using Xunit;

namespace MadWizard.Desomnia.Processes.Tests
{
    /// <summary>
    /// A threshold is only worth configuring where something keeps the counter behind it. Each
    /// platform states which of them it keeps, and the validation rides the resolve pipeline of
    /// every watch registration: a watch asked for a counter this machine has no source for fails
    /// to resolve, rather than being built and quietly guarding nothing for the lifetime of the
    /// service.
    /// </summary>
    public class ProcessMetricValidationTests
    {
        private static readonly ProcessingThreshold TenMilliseconds = new(TimeSpan.FromMilliseconds(10));

        private static readonly TransmissionThreshold OneMegabyte = new() { Amount = 1, ByteUnit = 1L << 20 };

        private static ProcessWatchInfo Info(ProcessingThreshold? minGPU = null, ProcessingThreshold? minCPU = null,
            TransmissionThreshold? minIO = null, TransmissionThreshold? minTraffic = null)
        {
            return new ProcessWatchInfo("game") { Name = "Game", MinGPU = minGPU, MinCPU = minCPU, MinIO = minIO, MinTraffic = minTraffic };
        }

        /// <summary>
        /// The container as the module builds it: the platform's counters, the watch registrations,
        /// and the hook that puts the validation on every one of them.
        /// </summary>
        private static IContainer Container(ProcessMetric supported, ProcessMetric shared = ProcessMetric.None,
            IProcessManager? manager = null)
        {
            var builder = new ContainerBuilder();

            builder.RegisterInstance(new FakeMetricSupport(supported, shared)).As<IProcessMetricSupport>();
            builder.RegisterInstance(manager ?? new FakeProcessSource()).As<IProcessManager>();

            builder.ComponentRegistryBuilder.Registered += (sender, args) =>
            {
                if (args.ComponentRegistration.IsLimitedTo<ProcessWatch>())
                    args.ComponentRegistration.PipelineBuilding += (_, pipeline) =>
                        pipeline.Use(new ProcessMetricWatchBuilder());
            };

            builder.RegisterType<PatternProcessWatch>().PropertiesAutowired().AsSelf();

            return builder.Build();
        }

        private static PatternProcessWatch Resolve(IComponentContext scope, ProcessWatchInfo info)
        {
            return scope.Resolve<PatternProcessWatch>(TypedParameter.From(info));
        }

        /// <summary>The exception the resolve failed with, dug out of Autofac's wrapping.</summary>
        private static Exception Cause(Exception thrown)
        {
            return thrown is DependencyResolutionException ? thrown.GetBaseException() : thrown;
        }

        [Fact]
        public void ThresholdWithoutACounter_FailsTheResolve()
        {
            using var container = Container(ProcessMetric.Processor | ProcessMetric.Storage);

            var thrown = Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minGPU: TenMilliseconds)));

            var cause = Cause(thrown);

            Assert.IsType<PlatformNotSupportedException>(cause);
            Assert.Contains("minGPU", cause.Message);
        }

        [Fact]
        public void EveryAttribute_IsRefusedByItsOwnCounter()
        {
            using var container = Container(ProcessMetric.None);

            Assert.IsType<PlatformNotSupportedException>(Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minCPU: TenMilliseconds)))));
            Assert.IsType<PlatformNotSupportedException>(Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minGPU: TenMilliseconds)))));
            Assert.IsType<PlatformNotSupportedException>(Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minIO: OneMegabyte)))));
            Assert.IsType<PlatformNotSupportedException>(Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minTraffic: OneMegabyte)))));
        }

        [Fact]
        public void ThresholdWithACounter_Resolves()
        {
            using var container = Container(ProcessMetric.Graphics);

            Assert.NotNull(Resolve(container, Info(minGPU: TenMilliseconds)));
        }

        [Fact]
        public void SharedMetrics_AreSuppliedToTheResolvedMetricsWatch()
        {
            var app = new FakeProcess(101, "game") { Gpu = TimeSpan.Zero };
            var helper = new FakeProcess(102, "game") { Gpu = TimeSpan.Zero };
            var source = new FakeProcessSource(app, helper);
            var info = Info(minGPU: TenMilliseconds);
            using var container = Container(ProcessMetric.Graphics, ProcessMetric.Graphics, source);

            var watch = Resolve(container, info);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();

            Thread.Sleep(20);
            app.Gpu = helper.Gpu = TimeSpan.FromMilliseconds(8);

            // One shared 8ms clock is below 10ms; without the supplied shared flag the two
            // processes would be summed to 16ms and incorrectly satisfy the threshold.
            Assert.Empty(watch.Inspect(System.Diagnostics.Stopwatch.GetElapsedTime(started)));
            Assert.Equal(2, app.GpuSamples);
            Assert.Equal(2, helper.GpuSamples);
        }

        [Fact]
        public void Composite_CombinesSupportedAndSharedMetricsIndependently()
        {
            IProcessMetricSupport combined = new ProcessMetricSupportCollector([
                new FakeMetricSupport(ProcessMetric.Processor),
                new FakeMetricSupport(ProcessMetric.Graphics, ProcessMetric.Graphics),
                new FakeMetricSupport(ProcessMetric.Traffic),
            ]);

            Assert.Equal(ProcessMetric.Processor | ProcessMetric.Graphics | ProcessMetric.Traffic, combined.SupportedMetrics);
            Assert.Equal(ProcessMetric.Graphics, combined.SharedMetrics);
        }

        [Fact]
        public void OneUnsupportedAttributeAmongSupportedOnes_StillRefuses()
        {
            // refused whole: honouring the measurable half would leave the other silently out of a
            // tally the operator believes is chaining all of them
            using var container = Container(ProcessMetric.Processor);

            Assert.IsType<PlatformNotSupportedException>(
                Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minCPU: TenMilliseconds, minTraffic: OneMegabyte)))));
        }

        [Fact]
        public void UnitlessThreshold_FailsTheResolve()
        {
            // NetworkWatch reads a bare number as packets, which no process can count – and read
            // as bytes, a naked number per interval would be satisfied by noise
            using var container = Container(ProcessMetric.Storage | ProcessMetric.Traffic);

            var bare = new TransmissionThreshold { Amount = 500 };

            Assert.IsType<FormatException>(Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minIO: bare)))));
            Assert.IsType<FormatException>(Cause(Assert.ThrowsAny<Exception>(() => Resolve(container, Info(minTraffic: bare)))));
        }

        [Fact]
        public void UnitlessZeroThreshold_IsConfiguredButNeedsNoUnit()
        {
            var zero = new TransmissionThreshold { Amount = 0 };

            using var supported = Container(ProcessMetric.Storage | ProcessMetric.Traffic);
            Assert.NotNull(Resolve(supported, Info(minIO: zero, minTraffic: zero)));

            using var unsupported = Container(ProcessMetric.None);
            Assert.IsType<PlatformNotSupportedException>(
                Cause(Assert.ThrowsAny<Exception>(() => Resolve(unsupported, Info(minIO: zero)))));
        }

        [Fact]
        public void WithoutThresholds_ThePlatformIsNeverAsked()
        {
            // a watch that measures nothing needs no counter, and must not be refused by a
            // platform that keeps none
            using var container = Container(ProcessMetric.None);

            Assert.NotNull(Resolve(container, Info()));
        }

        /// <summary>
        /// The session module registers its watches in a per-session lifetime scope of its own, so
        /// the hook has to reach registrations the root builder never saw — otherwise a session's
        /// thresholds are the one place that goes unvalidated.
        /// </summary>
        [Fact]
        public void WatchRegisteredInAChildScope_IsValidatedToo()
        {
            using var container = Container(ProcessMetric.Processor);

            using var scope = container.BeginLifetimeScope("Session",
                builder => builder.RegisterType<ChildScopeProcessWatch>().PropertiesAutowired().AsSelf());

            var thrown = Assert.ThrowsAny<Exception>(
                () => scope.Resolve<ChildScopeProcessWatch>(TypedParameter.From<ProcessWatchMetrics>(Info(minGPU: TenMilliseconds))));

            Assert.IsType<PlatformNotSupportedException>(Cause(thrown));
        }

        [Fact]
        public void WatchRegisteredInAChildScope_ResolvesWhatThePlatformKeeps()
        {
            using var container = Container(ProcessMetric.Processor);

            using var scope = container.BeginLifetimeScope("Session",
                builder => builder.RegisterType<ChildScopeProcessWatch>().PropertiesAutowired().AsSelf());

            Assert.NotNull(scope.Resolve<ChildScopeProcessWatch>(TypedParameter.From<ProcessWatchMetrics>(Info(minCPU: TenMilliseconds))));
        }

        /// <summary>Stands in for AnySessionProcessWatch: registered per session, not by the module.</summary>
        private sealed class ChildScopeProcessWatch : ProcessWatch
        {
            public ChildScopeProcessWatch(ProcessWatchMetrics metrics) : base("child") { }

            protected override bool ShouldWatchProcess(IProcess process) => true;
        }

        [Fact]
        public void TheDefaultSupport_MeasuresTheProcessorClockAlone()
        {
            // what ProcessHandle answers when no platform took over: the BCL clock, and nothing else
            Assert.Equal(ProcessMetric.Processor, new DefaultProcessMetricSupport().SupportedMetrics);
        }
    }
}
