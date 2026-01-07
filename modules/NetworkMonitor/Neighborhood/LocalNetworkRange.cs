using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class LocalNetworkRange : NetworkHostRange
    {
        public required NetworkDevice Device { private get; init; }

        public override bool Contains(IPAddress ip)
        {
            foreach (var unicast in Device.Interface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != ip.AddressFamily)
                    continue;

                if (ip.IsInSameSubnet(unicast.Address, unicast.PrefixLength))
                    return true;
            }

            return false;
        }
    }
}
