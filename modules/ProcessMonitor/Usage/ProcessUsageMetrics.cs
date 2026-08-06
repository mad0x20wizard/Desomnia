using System.Globalization;

namespace MadWizard.Desomnia.Processes
{
    public class ProcessUsageMetrics
    {
        /// <summary>The group's share of the interval, when minCPU compared a percentage.</summary>
        public double? ProcessingUsage { get; init; }
        /// <summary>The processor time consumed in the interval, when minCPU compared a time.</summary>
        public TimeSpan? ProcessingTime { get; init; }

        /// <summary>The group's graphics share of the interval, when minGPU compared a percentage.</summary>
        public double? GraphicsProcessingUsage { get; init; }
        /// <summary>The graphics time consumed in the interval, when minGPU compared a time.</summary>
        public TimeSpan? GraphicsProcessingTime { get; init; }

        /// <summary>Storage bytes the group moved during the interval, when an absolute minIO threshold measured them.</summary>
        public long? Storage { get; init; }
        /// <summary>Storage bytes per second, when a minIO rate was compared instead.</summary>
        public double? StorageRate { get; init; }

        /// <summary>Network bytes the group transferred during the interval, when an absolute minTraffic threshold measured them.</summary>
        public long? Traffic { get; init; }
        /// <summary>Network bytes per second, when a minTraffic rate was compared instead.</summary>
        public double? TrafficRate { get; init; }

        private static string Percentage(double usage) => string.Create(CultureInfo.InvariantCulture, $"{usage * 100:0.#}%");

        /// <summary>Renders whatever was measured, in a fixed order: CPU, GPU, storage, then traffic.</summary>
        public override string ToString()
        {
            var parts = new List<string>(4);

            if (ProcessingUsage is double usage)
                parts.Add($"CPU={Percentage(usage)}");
            else if (ProcessingTime is TimeSpan time)
                parts.Add($"CPU={time}");

            if (GraphicsProcessingUsage is double graphics)
                parts.Add($"GPU={Percentage(graphics)}");
            else if (GraphicsProcessingTime is TimeSpan graphicsTime)
                parts.Add($"GPU={graphicsTime}");

            if (Storage is long storage)
                parts.Add($"IO={IOFormat.Bytes(storage)}");
            else if (StorageRate is double storageRate)
                parts.Add($"IO={IOFormat.Bytes(storageRate)}/s");

            if (Traffic is long traffic)
                parts.Add($"Tx={IOFormat.Bytes(traffic)}");
            else if (TrafficRate is double trafficRate)
                parts.Add($"Tx={IOFormat.BitsPerSecond(trafficRate)}"); // a speed is spoken in bits, an amount in bytes

            return string.Join(" | ", parts);
        }
    }
}
