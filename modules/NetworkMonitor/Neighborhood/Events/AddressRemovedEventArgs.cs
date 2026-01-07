using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class AddressRemovedEventArgs(IPAddress ip, bool expired = false) : AddressEventArgs(ip)
    {
        public bool HasExpired => expired;
    }
}
