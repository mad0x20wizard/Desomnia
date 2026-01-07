using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class AddressAddedEventArgs(IPAddress ip, DateTime? expires) : AddressEventArgs(ip)
    {
        public DateTime? Expires => expires;
    }
}
