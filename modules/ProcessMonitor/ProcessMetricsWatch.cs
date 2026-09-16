using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Processes.Configuration;
using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Processes.Metrics;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Processes
{
    public class ProcessMetricsWatch(ProcessWatchMetrics metrics, ProcessMetric shared = ProcessMetric.None)
    {
        private readonly Lock _lock = new();
        private readonly HashSet<IProcess> _processes = [];

        private readonly Dictionary<IProcess, Sample<TimeSpan>> _lastProcessorTime = [];
        private readonly Dictionary<IProcess, Sample<TimeSpan>> _lastGraphicsTime = [];
        private readonly Dictionary<IProcess, Sample<ProcessInputOutput>> _lastIO = [];
        private readonly Dictionary<IProcess, Sample<ProcessInputOutput>> _lastTraffic = [];

        private readonly record struct Sample<T>(T Value, long Timestamp);

        public ProcessWatchMetrics Metrics => metrics;

        internal void Track(IProcess process)
        {
            lock (_lock)
            {
                if (!_processes.Add(process))
                    return;

                if (metrics.MinCPU is not null)
                    Start(process, _lastProcessorTime, static process => process.ProcessorTime);
                if (metrics.MinGPU is not null)
                    Start(process, _lastGraphicsTime, static process => process.GraphicsProcessorTime);
                if (metrics.MinIO is not null)
                    Start(process, _lastIO, static process => process.StorageData);
                if (metrics.MinTraffic is not null)
                    Start(process, _lastTraffic, static process => process.NetworkData);
            }
        }

        internal void Untrack(IProcess process)
        {
            lock (_lock)
            {
                if (!_processes.Remove(process))
                    return;

                _lastProcessorTime.Remove(process);
                _lastGraphicsTime.Remove(process);
                _lastIO.Remove(process);
                _lastTraffic.Remove(process);
            }
        }

        internal ProcessMetricsUsage? TakeMeasurement(TimeSpan sampleDuration)
        {
            if (sampleDuration <= TimeSpan.Zero)
                throw new ArgumentException("TakeMeasurement(): SampleDuration =< 0");

            TimeSpan? processor = null, graphics = null;
            long? storage = null, traffic = null;
            int processCount;

            lock (_lock)
            {
                processCount = _processes.Count;

                if (processCount > 0 && metrics.MinCPU is not null)
                    processor = MeasureTime(_lastProcessorTime, sampleDuration, static process => process.ProcessorTime, distinct: shared.HasFlag(ProcessMetric.Processor));
                if (processCount > 0 && metrics.MinGPU is not null)
                    graphics = MeasureTime(_lastGraphicsTime, sampleDuration, static process => process.GraphicsProcessorTime, distinct: shared.HasFlag(ProcessMetric.Graphics));
                if (processCount > 0 && metrics.MinIO is not null)
                    storage = MeasureBytes(_lastIO, sampleDuration, static process => process.StorageData);
                if (processCount > 0 && metrics.MinTraffic is not null)
                    traffic = MeasureBytes(_lastTraffic, sampleDuration, static process => process.NetworkData);
            }

            var watchMetrics = new MetricsUsage();

            ProcessingMetric? Match(string name, ProcessingThreshold? threshold, TimeSpan? value, TimeSpan capacity)
            {
                if (threshold is not ProcessingThreshold minimum)
                    return null;

                if (processCount == 0)
                {
                    watchMetrics.Add(name, false);
                    return null;
                }
                if (value is not TimeSpan consumed)
                    throw new InvalidOperationException($"Configured process metric '{name}' cannot be read.");

                if (minimum.AbsoluteTime is TimeSpan absolute)
                {
                    var matches = absolute == TimeSpan.Zero || consumed > absolute;
                    watchMetrics.Add(name, matches);
                    return matches ? new(consumed, capacity, ProcessingMetricFormat.Time) : null;
                }
                if (minimum.RelativeUsage is double relative)
                {
                    var result = new ProcessingMetric(consumed, capacity, ProcessingMetricFormat.Percentage);
                    var matches = relative == 0 || result.Usage > relative;
                    watchMetrics.Add(name, matches);
                    return matches ? result : null;
                }

                throw new InvalidOperationException($"Configured process metric '{name}' has no threshold value.");
            }

            TransferMetric? MatchTransfer(string name, TransmissionThreshold? threshold, long? value, TransferMetricFormat rateFormat)
            {
                if (threshold is not TransmissionThreshold minimum)
                    return null;

                if (processCount == 0)
                {
                    watchMetrics.Add(name, false);
                    return null;
                }
                if (value is not long bytes)
                    throw new InvalidOperationException($"Configured process metric '{name}' cannot be read.");

                var format = minimum.TimeUnit is null ? TransferMetricFormat.Bytes : rateFormat;
                var matches = Satisfies(minimum, bytes, sampleDuration);
                watchMetrics.Add(name, matches);

                return matches ? new(bytes, format) : null;
            }

            var result = new ProcessMetricsUsage(sampleDuration)
            {
                Processor = Match("CPU", metrics.MinCPU, processor, ProcessorCapacity(sampleDuration)),
                // One engine busy for the full sample is 100%; simultaneous engines may exceed it.
                GraphicsProcessor = Match("GPU", metrics.MinGPU, graphics, sampleDuration),
                Storage = MatchTransfer("IO", metrics.MinIO, storage, TransferMetricFormat.BytesPerSecond),
                Traffic = MatchTransfer("Traffic", metrics.MinTraffic, traffic, TransferMetricFormat.BitsPerSecond),
            };

            result.AddRange(watchMetrics);

            return metrics.Watch.IsYield || metrics.Watch.Evaluate(result) ? result : null;
        }

        private void Start<T>(IProcess process, Dictionary<IProcess, Sample<T>> history,
            Func<IProcess, T?> counter) where T : struct
        {
            if (counter(process) is T value)
                history[process] = new(value, Stopwatch.GetTimestamp());
        }

        /// <summary>Returns normalized interval deltas, optionally counting equal values only once.</summary>
        private TimeSpan? MeasureTime(Dictionary<IProcess, Sample<TimeSpan>> history,
            TimeSpan sampleDuration, Func<IProcess, TimeSpan?> clock, bool distinct = false)
        {
            HashSet<TimeSpan>? values = distinct ? [] : null;
            var consumed = TimeSpan.Zero;
            var sampled = false;

            foreach (var process in _processes)
            {
                if (clock(process) is not TimeSpan total)
                    continue;

                var timestamp = Stopwatch.GetTimestamp();
                sampled = true;

                if ((values is null || values.Add(total))
                    && history.TryGetValue(process, out var last) && total > last.Value)
                {
                    consumed += Extrapolate(total - last.Value, last.Timestamp, timestamp, sampleDuration);
                }

                history[process] = new(total, timestamp);
            }

            return sampled ? consumed : null;
        }

        /// <summary>Returns normalized interval deltas for each process' counters, or null when none answered.</summary>
        private long? MeasureBytes(Dictionary<IProcess, Sample<ProcessInputOutput>> history,
            TimeSpan sampleDuration,
            Func<IProcess, ProcessInputOutput?> counters)
        {
            long bytes = 0;
            var sampled = false;

            foreach (var process in _processes)
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

                history[process] = new(total, timestamp);
            }

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
            if (threshold.Amount == 0)
                return true;

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
