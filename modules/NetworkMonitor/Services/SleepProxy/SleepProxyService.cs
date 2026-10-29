using MadWizard.Desomnia.Network.Neighborhood;
using System.Net;

namespace MadWizard.Desomnia.Network.SleepProxy
{
    internal class SleepProxyService(ushort port) : NetworkService("SleepProxy", new IPService(IPProtocol.UDP, port))
    {
        public override string ServiceName => "sleep-proxy";

        public required SleepProxyMetrics Metrics { get; set; }
    }
}
