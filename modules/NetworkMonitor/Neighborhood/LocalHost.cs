using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    internal class LocalHost(NetworkDevice device) : NetworkHost(Dns.GetHostName())
    {
        public override string Name => Dns.GetHostName();

        public override PhysicalAddress? PhysicalAddress => device.PhysicalAddress;

        public override IEnumerable<IPAddress> IPAddresses => device.IPAddresses;
    }
}