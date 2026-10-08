using MadWizard.Desomnia.Network.Manager;
using System.Diagnostics.CodeAnalysis;

namespace SharpPcap
{
    internal static class SharpPcapExt
    {
        extension (CaptureDeviceList list)
        {
            internal bool TryFindByIdentity(NetworkIdentity identity, [NotNullWhen(true)] out ILiveDevice? device)
            {
                var deviceName = $@"\Device\NPF_{identity.Id}";

                device = list.FirstOrDefault(device => string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase));

                return device is not null;
            }
        }
    }
}
