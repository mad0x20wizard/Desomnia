using MadWizard.Desomnia.Configuration;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Processes.Configuration
{
    // The pattern is mandatory: the only constructor takes it as XML text content
    // (or as a "pattern" attribute, which the binder maps to the constructor parameter).
    public class ProcessWatchInfo(string pattern) // <- XML text content
    {
        public required string Name { get; set; }

        public bool IsFilePathPattern => pattern.Contains("\\\\") || pattern.Contains('/');

        public bool WatchChildren { get; set; } = false;

        public Regex Pattern { get; } = new(pattern);

        public DelayedActionInfo? OnIdle { get; set; }
        public DelayedActionInfo? OnDemand { get; set; }

        public DelayedActionInfo? OnStart { get; set; }
        public DelayedActionInfo? OnStop { get; set; }

        public CPUThreshold? MinCPU { get; set; }

        public IOThreshold? MinIO { get; set; }
        public IOThreshold? MinTraffic { get; set; }

        /// <summary>
        /// Whether any threshold gates this group's demand. Kept beside the properties on purpose:
        /// a new min-attribute added here cannot miss the watch's existence shortcut, which once
        /// silently ignored a threshold this list did not know about.
        /// </summary>
        public bool HasThresholds => MinCPU is not null || MinIO is not null || MinTraffic is not null;
    }
}
