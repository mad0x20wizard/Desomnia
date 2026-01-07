using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Filter.Rules;
using System.Net;

namespace MadWizard.Desomnia.Network.Configuration.Services
{
    public class HTTPServiceInfo : ServiceInfo
    {
        public HTTPServiceInfo()
        {
            Name = "HTTP";
            Protocol = IPProtocol.TCP;
            Port = 80;
        }

        public IList<HTTPRequestFilterRuleInfo>? RequestFilterRule { get; set; }

        protected override ServiceFilterRuleInfo ToFilterRule()
        {
            return new HTTPFilterRuleInfo
            {
                Name = Name,
                Protocol = Protocol,
                Port = Port,

                HostFilterRule = HostFilterRule,
                RequestFilterRule = RequestFilterRule,

                Type = FilterRuleType.Must
            };
        }

    }
}
