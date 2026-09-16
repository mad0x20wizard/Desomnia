namespace MadWizard.Desomnia.Network
{
    public abstract class NetworkUsage(long bytes) : UsageToken
    {
        public long Bytes => bytes;

        /// <summary>
        /// Bytes per second over the inspected interval, when the threshold compared a speed
        /// rather than an amount – null where it did not, and the bytes alone are the answer.
        /// </summary>
        public double? Rate { get; init; }
    }
}
