using MadWizard.Desomnia.Network.Filter.Rules;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class HTTPRequestFilterRuleInfo
    {
        public IList<HTTPRequestFilterRuleInfo> RequestFilterRule { get; set; } = [];

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
            public HTTPHeaderInfo() { }

            public HTTPHeaderInfo(string text) // <- XML text content
            {
                Text = text;
            }

            public required string Name { get; set; }
            public string? Text { get; set; }
        }

        public class HTTPUserAgentInfo : HTTPHeaderInfo
        {
            public HTTPUserAgentInfo()
            {
                Name = "User-Agent"; // LATER add attributes to HTTPUserAgentInfo
            }

            public HTTPUserAgentInfo(string text) : this() // <- XML text content
            {
                Text = text;
            }
        }

        public class HTTPCookieInfo
        {
            public HTTPCookieInfo() { }

            public HTTPCookieInfo(string value) // <- XML text content
            {
                Value = value;
            }

            public required string Name { get; set; }
            public string? Value { get; private set; }
        }

        public static implicit operator HTTPRequestPattern(HTTPRequestFilterRuleInfo info)
        {
            return new()
            {
                Method  = info.Method,
                Path    = info.Path,
                Version = info.Version,
                Host    = info.Host,
                Header  = (info.Headers ?? []).ToDictionary(header => header.Name, header => header.Text, StringComparer.OrdinalIgnoreCase),
                Cookie  = info.Cookie.ToDictionary(cookie => cookie.Name, cookie => cookie.Value, StringComparer.Ordinal)
            };
        }
    }
}
