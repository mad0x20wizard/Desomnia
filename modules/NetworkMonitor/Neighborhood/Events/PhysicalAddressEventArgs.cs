using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class PhysicalAddressEventArgs(PhysicalAddress mac) : EventArgs
    {
        public PhysicalAddress PhysicalAddress => mac;
    }
}
