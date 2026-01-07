namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class NetworkHostRangeInfo : IPAddressRangeInfo
    {
        public required string Name { get; init; }

        // or combine multiple ranges into one:
        public IList<NetworkHostInfo> Host { get; private set; } = [];
        public IList<IPAddressRangeInfo> HostRange { get; private set; } = [];
    }
}
