using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Processes.Configuration
{
    public record ProcessWatchMetrics : WatchMetrics
    {
        public ProcessWatchMetrics() : this(WatchExpression.DefaultAND) { }

        public ProcessWatchMetrics(WatchExpression expression)
        {
            Watch = expression;
        }

        public ProcessingThreshold?     MinCPU      { get; init; }
        public ProcessingThreshold?     MinGPU      { get; init; }

        public TransmissionThreshold?   MinIO       { get; init; }
        public TransmissionThreshold?   MinTraffic  { get; init; }

        /// <summary>
        /// Whether any threshold gates this group's demand. Kept beside the properties on purpose:
        /// a new min-attribute added here cannot miss the watch's existence shortcut, which once
        /// silently ignored a threshold this list did not know about.
        /// </summary>
        public bool HasThresholds =>
            MinCPU      is not null ||
            MinGPU      is not null ||
            MinIO       is not null ||
            MinTraffic  is not null;
    }
}
