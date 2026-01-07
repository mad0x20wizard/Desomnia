using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class IPAddressInfo
    {
        public IPAddress? IPv4 { get; set; }
        public IPAddress? IPv6 { get; set; }

        public virtual IEnumerable<IPAddress> IPAddresses
        {
            get
            {
                if (IPv4 != null)
                    yield return IPv4;
                if (IPv6 != null)
                    yield return IPv6;
            }
        }
    }
}
