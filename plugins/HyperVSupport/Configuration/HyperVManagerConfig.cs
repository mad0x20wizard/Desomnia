namespace MadWizard.Desomnia.Network.HyperV.Configuration
{
    public class HyperVManagerConfig
    {
        public VirtualTraffic WatchVirtualTraffic
        {
            get;
            set
            {
                if ((value & ~(VirtualTraffic.Internal | VirtualTraffic.External)) != 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown virtual traffic mode.");
                field = value;
            }
        } = VirtualTraffic.Internal | VirtualTraffic.External;
    }
}
