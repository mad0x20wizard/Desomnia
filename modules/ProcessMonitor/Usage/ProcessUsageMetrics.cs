using MadWizard.Desomnia.Processes.Metrics;

namespace MadWizard.Desomnia.Processes
{
    public class ProcessUsageMetrics
    {
        public ProcessUsageMetrics(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero, nameof(duration));

            SampleDuration = duration;
        }

        public TimeSpan             SampleDuration      { get; }

        public ProcessingMetric?    Processor           { get; init; }
        public ProcessingMetric?    GraphicsProcessor   { get; init; }
        public TransferMetric?      Storage             { get; init; }
        public TransferMetric?      Traffic             { get; init; }

        /// <summary>
        ///     Combines measurements from the same sample, retaining the greatest value of each metric.
        /// </summary>
        public static ProcessUsageMetrics operator |(ProcessUsageMetrics? left, ProcessUsageMetrics right)
        {
            if (left is null)
                return right;
            if (left.SampleDuration != right.SampleDuration)
                throw new ArgumentException("Cannot combine metrics from different sample durations.", nameof(right));

            static T? Highest<T, V>(T? left, T? right, Func<T, V> value) where T : struct where V : IComparable<V>
            {
                if (left is not T)
                    return right;
                if (right is not T)
                    return left;

                return value(left.Value).CompareTo(value(right.Value)) >= 0 ? left.Value : right.Value;
            }

            return new (left.SampleDuration)
            {
                Processor           = Highest(left.Processor,           right.Processor,            static metric => metric.Time),
                GraphicsProcessor   = Highest(left.GraphicsProcessor,   right.GraphicsProcessor,    static metric => metric.Time),
                Storage             = Highest(left.Storage,             right.Storage,              static metric => metric.Bytes),
                Traffic             = Highest(left.Traffic,             right.Traffic,              static metric => metric.Bytes),
            };
        }

        public override string ToString()
        {
            var parts = new List<string>(4);

            if (Processor is ProcessingMetric processor)
                parts.Add($"CPU={processor.ToString()}");
            if (GraphicsProcessor is ProcessingMetric graphics)
                parts.Add($"GPU={graphics.ToString()}");
            if (Storage is TransferMetric storage)
                parts.Add($"IO={storage.ToString(SampleDuration)}");
            if (Traffic is TransferMetric traffic)
                parts.Add($"Tx={traffic.ToString(SampleDuration)}");

            return string.Join(" | ", parts);
        }
    }
}
