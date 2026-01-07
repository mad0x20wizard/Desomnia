using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Filter.Rules;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Services;
using System.Data;
using System.Net;

namespace MadWizard.Desomnia.Network.Middleware
{
    /**
     * When services are configured int the config file,
     * the corresponding filter rules are automatically added
     * to the set of rules for the host.
     * 
     * When services are added dynamically during runtime,
     * the missing filter rules can result in the service
     * not triggering a DemandEvent at all.
     * 
     * This middleware tries to fill the gap for these services.
     */
    internal class DynamicPacketFilterRules : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.Sharing;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            next(context);

            var rules = (IEnumerable<PacketFilterRule>)context.Instance!; // statically configured rules
            var services = context.Resolve<IEnumerable<NetworkService>>(); // potentially dynamic list of services

            context.Instance = rules.Union(ComplementMissingRules(rules, services));
        }

        private static IEnumerable<PacketFilterRule> ComplementMissingRules(IEnumerable<PacketFilterRule> rules, IEnumerable<NetworkService> services)
        {
            var transportRules = rules.OfType<TransportFilterRule>().Where(rule => rule.Type == FilterRuleType.Must);
            var transportServices = services.OfType<TransportNetworkService>();

            foreach (var service in transportServices)
            {
                if (!transportRules.Any(rule => service.Serves(rule.Port)))
                {
                    yield return new GenericServiceFilterRule(service) { Type = FilterRuleType.May };
                }
                else
                {
                    foreach (var port in service.Ports)
                    {
                        if (!transportRules.Any(rule => rule.Port == port))
                        {
                            switch (port.Protocol)
                            {
                                case IPProtocol.TCP:
                                    yield return new TCPServiceFilterRule(port.Port) { Type = FilterRuleType.May };
                                    break;

                                case IPProtocol.UDP:
                                    yield return new UDPServiceFilterRule(port.Port) { Type = FilterRuleType.May };
                                    break;

                                default:
                                    throw new NotSupportedException($"Protocol {port.Protocol} is not supported.");
                            }
                        }
                    }
                }
            }
        }
    }
}
