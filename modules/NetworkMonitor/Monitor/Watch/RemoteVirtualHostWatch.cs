using System.Net;

namespace MadWizard.Desomnia.Network
{
    public class RemoteVirtualHostWatch : RemoteHostWatch
    {
        public required RemoteHostWatch PhysicalWatch { private get; init; }

        protected internal override async Task WakeUp(IPAddress? ip = null)
        {
            if (!await Reachability.Test(PhysicalWatch, label: "physical host"))
            {
                await PhysicalWatch.WakeUp();
            }

            await base.WakeUp(ip);
        }
    }
}
