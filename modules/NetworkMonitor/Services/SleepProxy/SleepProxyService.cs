using MadWizard.Desomnia.Network.Neighborhood.Services;
using System.Net;

namespace MadWizard.Desomnia.Network.SleepProxy
{
    internal class SleepProxyService(ushort port) : TransportNetworkService("SleepProxy", new IPService(IPProtocol.UDP, port))
    {
        public override string ServiceName => "sleep-proxy";

        public required SleepProxyMetrics Metrics { get; set; }
    }
}
