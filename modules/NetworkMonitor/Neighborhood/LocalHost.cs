using MadWizard.Desomnia.Network.Device;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class LocalHost(NetworkDevice device) : NetworkHost(Dns.GetHostName())
    {
        public override string Name => Dns.GetHostName();

        public override PhysicalAddress? PhysicalAddress => device.PhysicalAddress;

        public override IEnumerable<IPAddress> IPAddresses => device.IPAddresses;

        public override IPAddressOptions this[IPAddress ip] => default;

        public override bool AddAddress(IPAddress ip, IPAddressOptions options = default)   => throw new InvalidOperationException("LocalHost");
        public override bool RemoveAddress(IPAddress ip, bool expired = false)              => throw new InvalidOperationException("LocalHost");

    }
}