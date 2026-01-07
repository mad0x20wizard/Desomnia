namespace MadWizard.Desomnia.Network.Neighborhood
{
    public class VirtualNetworkHost(string name) : NetworkHost(name)
    {
        public required NetworkHost PhysicalHost { get; init; }
    }
}
