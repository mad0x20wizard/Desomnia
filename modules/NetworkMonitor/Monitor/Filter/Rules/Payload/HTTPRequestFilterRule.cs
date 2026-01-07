namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public class HTTPRequestFilterRule : FilterRule
    {
        public required string? Method  { get; init; }
        public required string? Path    { get; init; }
        public required string? Version { get; init; }
        public required string? Host    { get; init; }

        public Dictionary<string, string?> Header { get; init; } = [];
        public Dictionary<string, string?> Cookie { get; init; } = [];

        public bool Matches(HTTPRequest request)
        {
            if (Method != null && !string.Equals(request.Method, Method, StringComparison.OrdinalIgnoreCase))
                return false;
            if (Path != null && !string.Equals(request.Path, Path, StringComparison.OrdinalIgnoreCase))
                return false;
            if (Version != null && !string.Equals(request.Version, Version, StringComparison.OrdinalIgnoreCase))
                return false;
            // LATER: implement headers and cookies
            return true;
        }
    }

    public class HTTPRequest
    {
        public required string Method   { get; init; }
        public required string Path     { get; init; }
        public required string Version  { get; init; }
        public required string Host     { get; init; }
    }
}
