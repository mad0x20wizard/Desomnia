using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Impersonation
{
    public struct AddressMapping
    {
        public IPAddress IPAddress { get; set; }
        public PhysicalAddress PhysicalAddress { get; set; }

        public AddressMapping(IPAddress ip, PhysicalAddress mac)
        {
            IPAddress = ip;
            PhysicalAddress = mac;
        }
    }
}