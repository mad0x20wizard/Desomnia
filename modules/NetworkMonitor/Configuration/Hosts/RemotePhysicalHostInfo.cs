namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class RemotePhysicalHostInfo : RemoteHostInfo
    {
        // Virtual-Hosts
        public IList<RemoteVirtualHostInfo> VirtualHost { get; private set; } = [];
    }
}
