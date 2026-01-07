using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Filter
{
    public class ServiceFilterRuleInfo : IPFilterRuleInfo
    {
        public string? Name { get; set; }

        public IPProtocol Protocol { get; set; } = IPProtocol.TCP;

        public ushort Port { get; set; }
    }
}
