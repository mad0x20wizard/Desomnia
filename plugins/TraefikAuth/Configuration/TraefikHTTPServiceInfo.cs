using MadWizard.Desomnia.Network.Configuration.Filter;

namespace MadWizard.Desomnia.Network.Traefik.Configuration
{
    public class TraefikHTTPServiceInfo
    {
        public required string Name { get; set; } = "HTTP";

        public string? TraefikAuthPrefix { get; set; }
        public string? TraefikURLRegex { get; set; }

        public IList<HTTPRequestFilterRuleInfo>? TraefikRequestFilterRule { get; set; }

    }
}
