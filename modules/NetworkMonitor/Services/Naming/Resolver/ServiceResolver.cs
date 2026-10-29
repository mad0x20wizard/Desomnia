using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Naming.Messages;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;

using static MadWizard.Desomnia.Network.Naming.Messages.DNSMessage;

namespace MadWizard.Desomnia.Network.Naming.Resolver
{
    internal class ServiceResolver : IMulticastDNSResolver
    {
        public required ILogger<ServiceResolver> Logger { private get; init; }

        public required NetworkMonitor Monitor { private get; init; }

        void IMulticastDNSResolver.Resolve(DNSQuery query)
        {
            var advertised = AdvertisedServices.ToArray();

            foreach (var question in query.Questions)
            {
                // Service-type enumeration (RFC 6763 §9): answer with one PTR per advertised service
                // *type*, so a browser learns which types exist before enumerating their instances.
                if (question.Name == MakaretuDnsExt.ServiceEnumeration && question.Type is DnsType.PTR or DnsType.ANY)
                {
                    foreach (var type in advertised.Select(s => s.service.LocalDomainName).Distinct())
                    {
                        query.AnswerWith(MakaretuDnsExt.ServiceEnumeration, type);
                    }

                    continue;
                }

                // A browse (PTR) for a type, or a targeted SRV/TXT for one of its instances.
                foreach (var (watch, service) in advertised)
                {
                    query.AnswerWith(question, watch.Host, service, options: new(watch.AdvertiseOptions, delay: true));
                }
            }
        }

        void IMulticastDNSResolver.Goodbye(DNSMessage goodbye)
        {
            // Withdraw all services on shutdown (TTL = 0, RFC 6762 §10.1).

            foreach (var (watch, service) in AdvertisedServices)
            {
                goodbye.AnswerWith(watch.Host, service, options: AnswerOptions.Goodbye);
            }
        }

        /// <summary>The transport services that watched hosts have asked us to advertise on their behalf.</summary>
        private IEnumerable<(HostDemandWatch watch, NetworkService service)> AdvertisedServices =>
            from    watch           in Monitor.OfType<HostDemandWatch>()
            from    serviceWatch    in watch

            where   serviceWatch.AdvertiseOptions.Type.HasFlag(AdvertiseType.Service) // should we advertise this service?

            select (watch, serviceWatch.Service);
    }
}
