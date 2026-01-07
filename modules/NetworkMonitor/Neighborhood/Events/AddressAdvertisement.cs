using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class AddressAdvertisement(IPAddress ip, TimeSpan? lifetime = null) : AddressEventArgs(ip)
    {
        public TimeSpan? Lifetime => lifetime;
    }
}
