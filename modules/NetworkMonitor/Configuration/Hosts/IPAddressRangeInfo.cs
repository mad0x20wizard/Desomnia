using NetTools;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class IPAddressRangeInfo
    {
        // either:
        private IPNetwork? Network  { get; set; }
        // or:
        private IPAddress? FirstIP  { get; set; }
        private IPAddress? LastIP   { get; set; }

        public IPAddressRange? AddressRange
        {
            get
            {
                if (Network is IPNetwork network)
                    return new IPAddressRange(network.BaseAddress, network.PrefixLength);
                else if (FirstIP is IPAddress firstIP && LastIP is IPAddress lastIP)
                    return new IPAddressRange(firstIP, lastIP);
                else
                    return null;
            }
        }
    }
}
