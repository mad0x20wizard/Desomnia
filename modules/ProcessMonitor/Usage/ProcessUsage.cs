namespace MadWizard.Desomnia.Processes
{
    public class ProcessUsage(string name) : UsageToken
    {
        public string Name => name;

        public double? Usage { get; init; }
        public TimeSpan? Time { get; init; }

        /// <summary>Storage bytes the group moved during the interval, when a minIO threshold measured them.</summary>
        public long? Storage { get; init; }

        /// <summary>Network bytes the group transferred during the interval, when a minTraffic threshold measured them.</summary>
        public long? Traffic { get; init; }

        public ProcessUsage(string name, double usage) : this(name)
        {
            this.Usage = usage;
        }

        public ProcessUsage(string name, TimeSpan time) : this(name)
        {
            this.Time = time;
        }

        /// <summary>Renders whatever was measured, in a fixed order: CPU, then storage bytes, then traffic.</summary>
        public string ToUsage()
        {
            var text = "";

            if (Usage is double usage)
                text += $":{usage * 100:0.0}%";
            else if (Time is TimeSpan time)
                text += $":{time}";

            if (Storage is long bytes)
                text += $":S={FormatBytes(bytes)}";

            if (Traffic is long traffic)
                text += $":T={FormatBytes(traffic)}";

            return text;
        }

        private static string FormatBytes(long bytes) => bytes switch
        {
            < 1L << 10 => $"{bytes}B",
            < 1L << 20 => $"{bytes / 1024.0:0.0}kB",
            < 1L << 30 => $"{bytes / 1048576.0:0.0}MB",
            _          => $"{bytes / 1073741824.0:0.0}GB"
        };

        public override string ToString() => "{" + Name + ToUsage() + "}";
    }
}
