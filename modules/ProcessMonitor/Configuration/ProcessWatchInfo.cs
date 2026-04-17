using MadWizard.Desomnia.Configuration;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Process.Configuration
{
    public class ProcessWatchInfo
    {
        public required string Name { get; set; }

        public Regex Pattern => Text != null ? new Regex(Text) : throw new ArgumentNullException("pattern");

        public DelayedAction? OnStart { get; set; }
        public DelayedAction? OnStop { get; set; }

        public bool WatchChildren { get; set; } = false;

        public CPUThreshold MinCPU { get; set; }

        private string? Text { get; set; }
    }
}
