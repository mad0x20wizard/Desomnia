namespace MadWizard.Desomnia.Network.Traefik.Configuration
{
    public class NetworkMonitorConfig
    {
        public string?      Name        { get; set; }

        internal ushort     TraefikAuthPort     { get; set; } = 5000;
        internal TimeSpan   TraefikAuthTimeout  { get; set; } = TimeSpan.FromSeconds(5);

        // Hosts
        public IList<TraefikServerInfo> RemoteHost { get; private set; } = [];
    }
}
