using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.PowerRequest.Configuration
{
    public class PowerRequestInfo
    {
        public required string Name { get; set; }

        private string? Text { get; set; }

        public Regex Pattern => Text != null ? new Regex(Text) : throw new ArgumentNullException("pattern");
    }
}
