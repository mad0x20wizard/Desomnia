using MadWizard.Desomnia.Network.Neighborhood.Options;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Handoff.Registration
{
    public abstract class HandoffRegistration(PhysicalAddress mac)
    {
        public PhysicalAddress PhysicalAddress { get; init; } = mac;
        public Dictionary<IPAddress, IPAddressOptions> IPAddresses { get; init; } = [];
        public List<HandoffServiceInfo> Services { get; init; } = [];

        /// <summary>Optional SecureOn Wake-on-LAN password.</summary>
        public byte[]? Password { get; set; }

        public bool Contains(IPService service)
        {
            return Services.Select(s => (IPService)s).Contains(service);
        }
    }
}
