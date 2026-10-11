using NetTools;

namespace MadWizard.Desomnia.Network.Configuration.Hosts
{
    public class NetworkHostRangeInfo : IPAddressRangeInfo
    {
        public required string Name { get; init; }

        // or combine multiple ranges into one:
        public IList<NetworkHostInfo> Host { get; private set; } = [];
        public IList<IPAddressRangeInfo> HostRange { get; private set; } = [];

        public override IEnumerator<IPAddressRange> GetEnumerator()
        {
            using (var baseEnum = base.GetEnumerator())
                while (baseEnum.MoveNext())
                    yield return baseEnum.Current;

            foreach (var ip in Host.SelectMany(host => host.IPAddresses).Distinct())
            {
                yield return new IPAddressRange(ip);
            }

            foreach (var hostRange in HostRange.SelectMany(hr => hr))
            {
                yield return hostRange;
            }
        }
    }
}
