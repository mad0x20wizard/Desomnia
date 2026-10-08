namespace MadWizard.Desomnia.Network.HyperV.Configuration
{
    [Flags]
    public enum VirtualTraffic
    {
        None        = 0,

        Private     = 1 << 0, // VM <-> VM (currently unsupported)

        Internal    = 1 << 1, // VM <-> Host
        External    = 1 << 2, // VM <-> Network
    }
}
