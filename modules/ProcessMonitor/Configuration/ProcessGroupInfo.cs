using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Process.Configuration
{
    public class ProcessGroupInfo
    {
        public required string Name { get; set; }

        public Regex Pattern => Text != null ? new Regex(Text) : throw new ArgumentNullException("pattern");

        public bool WithChildren { get; set; } = false;

        public double Threshold { get; set; }

        private string? Text { get; set; }
    }
}
