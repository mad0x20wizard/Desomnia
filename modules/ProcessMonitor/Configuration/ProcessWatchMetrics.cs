using MadWizard.Desomnia.Configuration;

namespace MadWizard.Desomnia.Processes.Configuration
{
    public class ProcessWatchMetrics
    {
        /// <summary>
        /// How the configured thresholds combine: with <c>and</c> – the default, and the reading
        /// they have always had – the group counts as busy only while it satisfies every one of
        /// them at once; with <c>or</c> any single one is enough. A group whose work moves between
        /// the metrics rather than doing all of them together (rendering without computing, or the
        /// reverse) needs <c>or</c>, or it reads as idle in the middle of the work.
        /// </summary>
        public Operator                 Min         { get; set; } = Operator.AND;

        public ProcessingThreshold?     MinCPU      { get; set; }
        public ProcessingThreshold?     MinGPU      { get; set; }

        public TransmissionThreshold?   MinIO       { get; set; }
        public TransmissionThreshold?   MinTraffic  { get; set; }

        /// <summary>
        /// Whether any threshold gates this group's demand. Kept beside the properties on purpose:
        /// a new min-attribute added here cannot miss the watch's existence shortcut, which once
        /// silently ignored a threshold this list did not know about.
        /// </summary>
        public bool HasThresholds => MinCPU is not null || MinGPU is not null || MinIO is not null || MinTraffic is not null;

        /// <summary>The default is AND, so an existing configuration keeps the behaviour it had.</summary>
        public enum Operator
        {
            AND = 0,
            OR = 1,
        }
    }
}
