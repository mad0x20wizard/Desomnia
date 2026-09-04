using MadWizard.Desomnia.Processes;

namespace MadWizard.Desomnia.Session
{
    public class SessionMetricsUsage : ProcessMetricsUsage
    {
        public SessionMetricsUsage(TimeSpan duration) : base(duration)
        {

        }

        public SessionMetricsUsage(ProcessMetricsUsage process) : this(process.SampleDuration)
        {
            Processor           = process.Processor;
            GraphicsProcessor   = process.GraphicsProcessor;
            Storage             = process.Storage;
            Traffic             = process.Traffic;

            base.AddRange(process);
        }

        public TimeSpan? LastInputTime { get; set; }

        public override string ToString()
        {
            string str = "";

            if (LastInputTime is TimeSpan time)
            {
                str += " ~ " + FormatTimeSpan(time);
            }

            if (base.ToString() is { Length: > 0 } parts)
            {
                str += " @ " + parts;
            }

            return str;
        }

        static string FormatTimeSpan(TimeSpan value)
        {
            int hours = (int)value.TotalHours;

            return hours != 0
                ? $"{hours}:{value.Minutes}:{value.Seconds:00}"
                : $"{value.Minutes}:{value.Seconds:00}";
        }
    }
}
