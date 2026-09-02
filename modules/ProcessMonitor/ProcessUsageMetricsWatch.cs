using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes
{
    internal class ProcessUsageMetricsWatch(ProcessWatchMetrics metrics)
    {
        private readonly Lock _historyLock = new();

        private Dictionary<object, Sample<TimeSpan>> _lastProcessorTime = [];
        private Dictionary<object, Sample<TimeSpan>> _lastGraphicsTime = [];
        private Dictionary<IProcess, Sample<ProcessInputOutput>> _lastIO = [];
        private Dictionary<IProcess, Sample<ProcessInputOutput>> _lastTraffic = [];

        private readonly record struct Sample<T>(T Value, long Timestamp);

        internal void Track(IProcess process)
        {
            lock (_historyLock)
            {
                if (metrics.MinCPU is not null)
                    Start(process, _lastProcessorTime, static process => process, static process => process.ProcessorTime);
                if (metrics.MinGPU is not null)
                    Start(process, _lastGraphicsTime, static process => process.GraphicsProcessorScope, static process => process.GraphicsProcessorTime);
                if (metrics.MinIO is not null)
                    Start(process, _lastIO, static process => process, static process => process.StorageData);
                if (metrics.MinTraffic is not null)
                    Start(process, _lastTraffic, static process => process, static process => process.NetworkData);
            }
        }

        internal ProcessUsageMetrics? TakeMeasurement(IProcess[] processes, TimeSpan sampleDuration)
        {
            if (sampleDuration <= TimeSpan.Zero)
                throw new ArgumentException("TakeMeasurement(): SampleDuration =< 0");

            TimeSpan? processor = null, graphics = null;
            long? storage = null, traffic = null;

            lock (_historyLock)
            {
                if (metrics.MinCPU is not null)
                    processor = MeasureTime(processes, ref _lastProcessorTime, sampleDuration, static process => process, static process => process.ProcessorTime) ?? TimeSpan.Zero;
                if (metrics.MinGPU is not null)
                    graphics = MeasureTime(processes, ref _lastGraphicsTime, sampleDuration, static process => process.GraphicsProcessorScope, static process => process.GraphicsProcessorTime);
                if (metrics.MinIO is not null)
                    storage = MeasureBytes(processes, ref _lastIO, sampleDuration, static process => process.StorageData);
                if (metrics.MinTraffic is not null)
                    traffic = MeasureBytes(processes, ref _lastTraffic, sampleDuration, static process => process.NetworkData);
            }

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

        private void Start<TKey, TValue>(IProcess process, Dictionary<TKey, Sample<TValue>> history,
            Func<IProcess, TKey> scope, Func<IProcess, TValue?> counter) where TKey : notnull where TValue : struct
        {
            var key = scope(process);

            if (!history.ContainsKey(key) && counter(process) is TValue value)
                history[key] = new(value, Stopwatch.GetTimestamp());
        }

        /// <summary>Returns normalized interval deltas for each distinct clock, or null when none answered.</summary>
        private TimeSpan? MeasureTime(IProcess[] processes, ref Dictionary<object, Sample<TimeSpan>> history,
            TimeSpan sampleDuration,
            Func<IProcess, object> scope, Func<IProcess, TimeSpan?> clock)
        {
            var measured = new Dictionary<object, Sample<TimeSpan>>(history.Count);
            var consumed = TimeSpan.Zero;
            var sampled = false;

            foreach (var process in processes)
            {
                var key = scope(process);

                if (measured.ContainsKey(key) || clock(process) is not TimeSpan total)
                    continue;

                var timestamp = Stopwatch.GetTimestamp();
                sampled = true;

                if (history.TryGetValue(key, out var last) && total > last.Value)
                {
                    consumed += Extrapolate(total - last.Value, last.Timestamp, timestamp, sampleDuration);
                }

                measured[key] = new(total, timestamp);
            }

            history = measured;

            return sampled ? consumed : null;
        }

        /// <summary>Returns normalized interval deltas for each process' counters, or null when none answered.</summary>
        private long? MeasureBytes(IProcess[] processes, ref Dictionary<IProcess, Sample<ProcessInputOutput>> history,
            TimeSpan sampleDuration,
            Func<IProcess, ProcessInputOutput?> counters)
        {
            var measured = new Dictionary<IProcess, Sample<ProcessInputOutput>>(history.Count);
            long bytes = 0;
            var sampled = false;

            foreach (var process in processes)
            {
                if (counters(process) is not ProcessInputOutput total)
                    continue;

                var timestamp = Stopwatch.GetTimestamp();
                sampled = true;

                if (history.TryGetValue(process, out var last))
                {
                    var transferred = Add(
                        Math.Max(0, total.BytesIn - last.Value.BytesIn),
                        Math.Max(0, total.BytesOut - last.Value.BytesOut));

                    bytes = Add(bytes, Extrapolate(transferred, last.Timestamp, timestamp, sampleDuration));
                }

                measured[process] = new(total, timestamp);
            }

            history = measured;

            return sampled ? bytes : null;
        }

        private static TimeSpan Elapsed(long start, long end) => Stopwatch.GetElapsedTime(start, end);

        private TimeSpan Extrapolate(TimeSpan value, long start, long end, TimeSpan sampleDuration)
        {
            var measuredDuration = Elapsed(start, end);

            if (measuredDuration <= TimeSpan.Zero)
                return value;

            var ticks = value.Ticks * (sampleDuration / measuredDuration);

            return ticks >= TimeSpan.MaxValue.Ticks ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)Math.Round(ticks));
        }

        private long Extrapolate(long value, long start, long end, TimeSpan sampleDuration)
        {
            var measuredDuration = Elapsed(start, end);

            if (measuredDuration <= TimeSpan.Zero)
                return value;

            var extrapolated = value * (sampleDuration / measuredDuration);

            return extrapolated >= long.MaxValue ? long.MaxValue : (long)Math.Round(extrapolated);
        }

        private static long Add(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;

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
