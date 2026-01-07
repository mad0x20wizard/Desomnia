using MadWizard.Desomnia.Network.Configuration.Options;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class NetworkHostInfo : IPAddressInfo
    {
        public required string  Name        { get; set; }
        public          string? HostName    { get; set; }

        public PhysicalAddress? MAC         { get; set; }

        public AutoDiscoveryType? AutoDetect { get; set; }
    }
}
