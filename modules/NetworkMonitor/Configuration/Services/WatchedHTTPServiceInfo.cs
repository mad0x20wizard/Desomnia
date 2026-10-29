using MadWizard.Desomnia.Network.Configuration.Filter;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Services
{
    public class WatchedHTTPServiceInfo : WatchedServiceInfo
    {
        public WatchedHTTPServiceInfo()
        {
            Name = "HTTP";
            Protocol = IPProtocol.TCP;
            Port = 80;
        }

        public IList<HTTPRequestFilterRuleInfo> RequestFilterRule { get; set; } = [];
    }
}
