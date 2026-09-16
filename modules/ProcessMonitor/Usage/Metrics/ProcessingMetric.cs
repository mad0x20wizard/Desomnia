using System.Globalization;

namespace MadWizard.Desomnia.Processes.Metrics
{
    /// <param name="Time">Processing time consumed during the sample.</param>
    /// <param name="TimeReference">Processing time corresponding to 100% during the sample.</param>
    /// <param name="Format">How to render the consumed time.</param>
    public readonly record struct ProcessingMetric(TimeSpan Time, TimeSpan TimeReference, ProcessingMetricFormat Format)
    {
        public readonly double Usage => Time.Ticks / (double)TimeReference.Ticks;

        public override string ToString()
        {
            switch (Format)
            {
                case ProcessingMetricFormat.Time:
                    return Time.ToString(); // e.g. 1:20:15
                case ProcessingMetricFormat.Percentage:
                    return string.Create(CultureInfo.InvariantCulture, $"{Usage * 100:0.#}%"); // e.g. 10.5%

                default: throw new InvalidOperationException($"Unknown {nameof(ProcessingMetricFormat)} = {Format}");
            }
        }
    }

    public enum ProcessingMetricFormat
    {
        Time,
        Percentage,
    }
}
