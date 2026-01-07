namespace MadWizard.Desomnia.Network.Configuration.Options
{
    public readonly struct DiscoveryOptions
    {
        public TimeSpan Timeout { get; init; }
        public TimeSpan? Latency { get; init; }
    }

    [Flags]
    public enum AutoDiscoveryType
    {
        None = 0,

        MAC = 1 << 1,

        IPv4 = 1 << 2,
        IPv6 = 1 << 3,

        Router = 1 << 10,
        VPN = 1 << 11,

        SleepProxy = 1 << 15,

        Services = 1 << 20,

        Everything = MAC | IPv4 | IPv6 | Router | VPN | SleepProxy | Services
    }
}
