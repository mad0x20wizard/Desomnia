using MadWizard.Desomnia.Configuration;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Processes.Configuration
{
    // The pattern is mandatory: the only constructor takes it as XML text content
    // (or as a "pattern" attribute, which the binder maps to the constructor parameter).
    public class ProcessWatchInfo(string pattern) : ProcessWatchMetrics // <- XML text content
    {
        public required string Name { get; set; }

        public Regex Pattern { get; } = new(pattern);

        public bool WatchChildren { get; set; } = false;

        #region Actions
        public DelayedActionInfo? OnIdle { get; set; }
        public DelayedActionInfo? OnDemand { get; set; }

        public DelayedActionInfo? OnStart { get; set; }
        public DelayedActionInfo? OnStop { get; set; }
        #endregion
    }
}
