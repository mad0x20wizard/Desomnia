using MadWizard.Desomnia.Network.Filter.Rules;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class HTTPFilterRuleInfo : ServiceFilterRuleInfo
    {
        public HTTPFilterRuleInfo()
        {
            Name = "HTTP";
            Protocol = IPProtocol.TCP;
            Port = 80;
        }

        public IList<HTTPRequestFilterRuleInfo>? RequestFilterRule { get; set; }
    }

    public class HTTPRequestFilterRuleInfo
    {
        public string? Method { get; set; } // e.g. "GET", "POST", etc.
        public string? Path { get; set; } // /index.html
        public string? Version { get; set; } // e.g. "HTTP/1.1", "HTTP/2.0", etc.
        public string? Host { get; set; } // e.g. "example.com"

        public HTTPUserAgentInfo? UserAgent { get; set; }
        public IList<HTTPHeaderInfo> Header { get; set; } = [];

        public IEnumerable<HTTPHeaderInfo>? Headers => Header.Concat(UserAgent != null ? [UserAgent] : []);

        public IList<HTTPCookieInfo> Cookie { get; set; } = [];

        public FilterRuleType Type { get; set; } = FilterRuleType.MustNot;

        public class HTTPHeaderInfo
        {
            public required string Name { get; set; }
            public string? Text { get; set; }
        }

        public class HTTPUserAgentInfo : HTTPHeaderInfo
        {
            public HTTPUserAgentInfo()
            {
                Name = "User-Agent"; // LATER add attributes to HTTPUserAgentInfo
            }
        }

        public class HTTPCookieInfo
        {
            public required string Name { get; set; }
            private string? Text { get; set; }
            public string? Value => Text;
        }
    }
}
