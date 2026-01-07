using System.Net;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.NetworkSession.Configuration
{
    public class NetworkSessionFilterRule
    {
        public string? UserName { get; set; }

        public string? ClientName { get; set; }
        public string? ClientIP { get; set; }

        public string? ShareName { get; set; }
        public string? FilePath { get; set; }

        public IPAddress? ClientIPAddress => ClientIP != null ? IPAddress.Parse(ClientIP) : null;
        public Regex? FilePathPattern => FilePath != null ? new Regex(FilePath) : null;

        public FilterType Type { get; set; } = FilterType.MustNot;
    }

    public enum FilterType
    {
        MustNot = 0,
        Must
    }
}
