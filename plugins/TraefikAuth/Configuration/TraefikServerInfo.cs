namespace MadWizard.Desomnia.Network.Traefik.Configuration
{
    public class TraefikServerInfo
    {
        public required string Name { get; set; }

        public IList<TraefikHTTPServiceInfo> HTTPService { get; set; } = [];
    }
}
