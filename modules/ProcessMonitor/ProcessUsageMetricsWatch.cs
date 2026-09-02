using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes
{
    internal class ProcessUsageMetricsWatch(ProcessWatchMetrics metrics)
    {
        private Dictionary<object, TimeSpan> _lastProcessorTime = [];
        private Dictionary<object, TimeSpan> _lastGraphicsTime = [];
        private Dictionary<IProcess, ProcessInputOutput> _lastIO = [];
        private Dictionary<IProcess, ProcessInputOutput> _lastTraffic = [];

        internal ProcessUsageMetrics? TakeMeasurement(IProcess[] processes, TimeSpan sampleDuration)
        {
            if (sampleDuration <= TimeSpan.Zero)
                sampleDuration = TimeSpan.FromMilliseconds(1);

            TimeSpan? processor = null, graphics = null;
            long? storage = null, traffic = null;

            if (metrics.MinCPU is not null)
                processor = MeasureTime(processes, ref _lastProcessorTime, static process => process, static process => process.ProcessorTime) ?? TimeSpan.Zero;
            if (metrics.MinGPU is not null)
                graphics = MeasureTime(processes, ref _lastGraphicsTime, static process => process.GraphicsProcessorScope, static process => process.GraphicsProcessorTime);
            if (metrics.MinIO is not null)
                storage = MeasureBytes(processes, ref _lastIO, static process => process.StorageData);
            if (metrics.MinTraffic is not null)
                traffic = MeasureBytes(processes, ref _lastTraffic, static process => process.NetworkData);

            if (processes.Length == 0)
                return null;

            var matchAll = metrics.Watch == WatchOperator.AND;
            var demand = matchAll;
            var measured = false;

            bool Include(bool matches)
            {
                measured = true;
                demand = matchAll ? demand & matches : demand | matches;

                return matches;
            }

            ProcessingMetric? Match(ProcessingThreshold? threshold, TimeSpan? value, TimeSpan capacity)
            {
                if (threshold is not ProcessingThreshold minimum || value is not TimeSpan consumed)
                    return null;

                if (minimum.AbsoluteTime is TimeSpan absolute)
                    return Include(consumed > absolute) ? new(consumed, capacity, ProcessingMetricFormat.Time) : null;
                if (minimum.RelativeUsage is double relative)
                {
                    var result = new ProcessingMetric(consumed, capacity, ProcessingMetricFormat.Percentage);

                    return Include(result.Usage > relative) ? result : null;
                }

                return null;
            }

            TransferMetric? MatchTransfer(TransmissionThreshold? threshold, long? value, TransferMetricFormat rateFormat)
            {
                if (threshold is not TransmissionThreshold minimum || value is not long bytes)
                    return null;

                var format = minimum.TimeUnit is null ? TransferMetricFormat.Bytes : rateFormat;

                return Include(Satisfies(minimum, bytes, sampleDuration)) ? new(bytes, format) : null;
            }

            var result = new ProcessUsageMetrics(sampleDuration)
            {
                Processor = Match(metrics.MinCPU, processor, ProcessorCapacity(sampleDuration)),
                // One engine busy for the full sample is 100%; simultaneous engines may exceed it.
                GraphicsProcessor = Match(metrics.MinGPU, graphics, sampleDuration),
                Storage = MatchTransfer(metrics.MinIO, storage, TransferMetricFormat.BytesPerSecond),
                Traffic = MatchTransfer(metrics.MinTraffic, traffic, TransferMetricFormat.BitsPerSecond),
            };

            // A wholly unreadable set fails open: missing counters must not make a working process idle.
            return demand || !measured ? result : null;
        }

        /// <summary>Returns the interval delta of each distinct clock, or null when none answered.</summary>
        private static TimeSpan? MeasureTime(IProcess[] processes, ref Dictionary<object, TimeSpan> history,
            Func<IProcess, object> scope, Func<IProcess, TimeSpan?> clock)
        {
            var measured = new Dictionary<object, TimeSpan>(history.Count);
            var consumed = TimeSpan.Zero;
            var sampled = false;

            foreach (var process in processes)
            {
                var key = scope(process);

                if (measured.ContainsKey(key) || clock(process) is not TimeSpan total)
                    continue;

                sampled = true;

                if (history.TryGetValue(key, out var last) && total > last)
                    consumed += total - last;

                measured[key] = total;
            }

            history = measured;

            return sampled ? consumed : null;
        }

        /// <summary>Returns the interval delta of each process' counters, or null when none answered.</summary>
        private static long? MeasureBytes(IProcess[] processes, ref Dictionary<IProcess, ProcessInputOutput> history,
            Func<IProcess, ProcessInputOutput?> counters)
        {
            var measured = new Dictionary<IProcess, ProcessInputOutput>(history.Count);
            long bytes = 0;
            var sampled = false;

            foreach (var process in processes)
            {
                if (counters(process) is not ProcessInputOutput total)
                    continue;

                sampled = true;

                if (history.TryGetValue(process, out var last))
                {
                    bytes += Math.Max(0, total.BytesIn - last.BytesIn)
                           + Math.Max(0, total.BytesOut - last.BytesOut);
                }

                measured[process] = total;
            }

            history = measured;

            return sampled ? bytes : null;
        }

        /// <summary>Windows reports total CPU capacity as all logical processors; macOS uses one core as 100%.</summary>
        private static TimeSpan ProcessorCapacity(TimeSpan sampleDuration) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? sampleDuration * Environment.ProcessorCount : sampleDuration;

        private static bool Satisfies(TransmissionThreshold threshold, long bytes, TimeSpan sampleDuration)
        {
            double value = bytes;
            double minimum = threshold.Amount * threshold.ByteUnit!.Value;

            if (threshold.TimeUnit is TimeSpan timeUnit)
            {
                value /= sampleDuration.TotalMilliseconds;
                minimum /= timeUnit.TotalMilliseconds;
            }

            return value >= minimum;
        }
    }
}
