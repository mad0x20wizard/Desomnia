using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class HTTPRequestFilterRule : FilterRule<IHTTPFilterContext, HTTPRequestFilterRule>
    {
        public required HTTPRequestPattern Pattern { get; init; }

        public override bool Matches(IHTTPFilterContext context)
        {
            if (Pattern.Matches(context))
            {
                base.Matches(context);
            }

            return false;
        }
    }

    public readonly struct HTTPRequestPattern
    {
        public string?  Version     { get; init; }

        public string?  Method      { get; init; }

        public string?  Host        { get; init; }
        public Regex?   HostRegex   { get; init; }

        public string?  Path        { get; init; }
        public string?  PathPrefix  { get; init; }
        public Regex?   PathRegex   { get; init; }

        public Dictionary<string, string?> Header { get; init; } = [];
        public Dictionary<string, string?> Cookie { get; init; } = [];

        public HTTPRequestPattern()
        {

        }

        public bool Matches(IHTTPFilterContext context)
        {
            return false; // TODO: implement
        }
    }
}
