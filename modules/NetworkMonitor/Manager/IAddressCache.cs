using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    public interface IAddressCache
    {
        public void Update(IPAddress ip, PhysicalAddress mac);
        public void Delete(IPAddress ip);
    }
}
