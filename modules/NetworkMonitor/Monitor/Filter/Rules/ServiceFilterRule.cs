using MadWizard.Desomnia.Network.Capture;
using System.Net;

namespace MadWizard.Desomnia.Network.Filter.Rules
{
    public abstract class ServiceFilterRule(IPService service) : IPFilterRule
    {
        public IPService Service => service;

        override public bool Matches(INetworkCapture packet, FlowDirection direction = FlowDirection.Inbound)
        {
            var endpoint = direction == FlowDirection.Inbound ? packet.TargetEndPoint : packet.SourceEndPoint;

            if (endpoint is IPServiceEndPoint endpointService)
            {
                if (service == endpointService)
                {
                    return base.Matches(packet, direction);
                }
            }

            return false;
        }
    }
}
