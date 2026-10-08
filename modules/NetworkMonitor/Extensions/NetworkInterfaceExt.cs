using MadWizard.Desomnia.Network.Manager;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    public static class NetworkInterfaceExt
    {
        public static NetworkIdentity ToIdentity(this NetworkInterface @interface)
        {
            string id = @interface.Id;

            if (OperatingSystem.IsWindows() && Guid.TryParse(@interface.Id, out var guid))
            {
                id = guid.ToString("B").ToUpperInvariant();
            }

            return new(id);
        }
    }
}
