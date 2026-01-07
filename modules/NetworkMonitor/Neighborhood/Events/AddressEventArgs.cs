using System.Net;

namespace MadWizard.Desomnia.Network.Neighborhood.Events
{
    public class AddressEventArgs(IPAddress ip) : EventArgs
    {
        public IPAddress IPAddress => ip;
    }
}
