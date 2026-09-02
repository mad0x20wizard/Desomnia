using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Processes.Middleware
{
    /**
     * Refuses a watch whose thresholds name a counter this machine does not keep.
     *
     * On the pipeline rather than in the watch, because it is not a question the watch has any part
     * in: it is asked once, of the configuration that built it, and never again. The watch measures
     * what it was given and holds no opinion about which platform it is running on – the platform
     * states its counters (<see cref="IProcessMetricSupport"/>), the configuration names the ones it
     * wants, and this is the one place the two meet.
     *
     * It rides every registration whose type descends from <see cref="ProcessWatch"/>, wherever that
     * registration was made – this module's own, the session module's per-session scope, a plugin's
     * – so a watch cannot come into existence without having been asked.
     *
     * Before activation, not after: a threshold nothing can measure is a configuration that will
     * never do what it says, and the resolve should fail rather than hand back a watch that quietly
     * guards nothing.
     */
    public sealed class ProcessMetricWatchBuilder : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            // the metrics arrive as the watch's own configuration – ProcessWatchInfo for a pattern
            // watch, a session's descriptor for the aggregate one – and both are the thresholds
            // this asks about. A watch built without any is simply not asking for a counter.
            if (context.FirstParameterOfType<ProcessWatchMetrics>() is ProcessWatchMetrics metrics)
            {
                ProcessUsageMetricsWatch? watch = null;

                if (metrics.HasThresholds)
                {
                    string name = metrics switch
                    {
                        ProcessMonitorConfig    => $"<ProcessMonitor>",
                        ProcessWatchInfo info   => $"<Process name=\"{info.Name}\">",
                        _                       => $"<{metrics.GetType().Name}>"
                    };

                    var platform = context.Resolve<IProcessMetricSupport>();

                    Validate(name, metrics, platform.SupportedMetrics);

                    watch = new ProcessUsageMetricsWatch(metrics, platform.SharedMetrics);
                }

                context.ChangeParameters([.. context.Parameters, TypedParameter.From(watch)]);
            }

            next(context);
        }

        /**
         * Refuses, at the moment the configuration is turned into a watch, a threshold this platform
         * has no counter for.
         *
         * Deliberately fatal rather than a warning: a threshold that cannot be measured cannot be
         * honoured either, and a machine guarded by an attribute nobody is measuring is worse than
         * one that refused to start – the log line for it would scroll past exactly once, months
         * before it mattered.
         */
        static void Validate(string name, ProcessWatchMetrics metrics, ProcessMetric supported)
        {
            Refuse(metrics.MinCPU       is not null, ProcessMetric.Processor,   nameof(metrics.MinCPU));
            Refuse(metrics.MinGPU       is not null, ProcessMetric.Graphics,    nameof(metrics.MinGPU));
            Refuse(metrics.MinIO        is not null, ProcessMetric.Storage,     nameof(metrics.MinIO));
            Refuse(metrics.MinTraffic   is not null, ProcessMetric.Traffic,     nameof(metrics.MinTraffic));

            // NetworkWatch reads a unit-less threshold as raw packets, which processes cannot
            // count – and read as bytes, a naked number per interval would be satisfied by noise.
            // Better to refuse loudly at load than to guard a machine against 500 bytes.
            RequireByteUnit(metrics.MinIO,      "minIO");
            RequireByteUnit(metrics.MinTraffic, "minTraffic");

            void Refuse(bool configured, ProcessMetric metric, string attribute)
            {
                if (configured && !supported.HasFlag(metric))
                {
                    attribute = char.ToLowerInvariant(attribute[0]) + attribute[1..];

                    throw new PlatformNotSupportedException($"{name}: '{attribute}' cannot be measured on this platform");
                }
            }

            void RequireByteUnit(TransmissionThreshold? threshold, string attribute)
            {
                if (threshold is TransmissionThreshold t && t.ByteUnit is null)
                    throw new FormatException($"'{name}': {attribute} requires a byte unit (e.g. \"100kb\" or \"1MB/s\")");
            }
        }
    }
}
