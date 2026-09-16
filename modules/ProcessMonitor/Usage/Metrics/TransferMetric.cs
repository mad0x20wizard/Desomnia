namespace MadWizard.Desomnia.Processes.Metrics
{
    /// <param name="Bytes">Bytes transferred during the sample.</param>
    /// <param name="Format">How to render the byte count.</param>
    public readonly record struct TransferMetric(long Bytes, TransferMetricFormat Format)
    {
        public string ToString(TimeSpan sampleDuration)
        {
            var bytesPerSecond = Bytes / sampleDuration.TotalSeconds;

            switch (Format)
            {
                case TransferMetricFormat.Bytes:
                    return IOFormat.Bytes(Bytes);
                case TransferMetricFormat.BytesPerSecond:
                    return $"{IOFormat.Bytes(bytesPerSecond)}/s";
                case TransferMetricFormat.BitsPerSecond:
                    return IOFormat.BitsPerSecond(bytesPerSecond);

                default: throw new InvalidOperationException($"Unknown {nameof(TransferMetricFormat)} = {Format}");
            }
        }
    }

    public enum TransferMetricFormat
    {
        Bytes,
        BytesPerSecond,
        BitsPerSecond,
    }
}
