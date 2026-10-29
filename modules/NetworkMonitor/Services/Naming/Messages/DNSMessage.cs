using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using Makaretu.Dns;
using System.Net;

namespace MadWizard.Desomnia.Network.Naming.Messages
{
    public class DNSMessage
    {
        internal Message Response { get; init; } = new Message() { QR = true, AA = true };

        internal TimeSpan Delay { get; private set; } = TimeSpan.Zero;

        public bool IsEmpty => Response.Answers.Count == 0;

        private void AnswerWith(ResourceRecord record, TimeSpan delay = default)
        {
            Response.Answers.Add(record);

            if (delay > Delay)
            {
                Delay = delay;
            }
        }

        /// <summary>Adds a bare PTR answer, e.g. for a DNS-SD service-type enumeration (RFC 6763 §9).</summary>
        public void AnswerWith(DomainName name, DomainName target, AnswerOptions options = default)
        {
            AnswerWith(new PTRRecord
            {
                Name = name,
                DomainName = target,

                TTL = options.ServiceTTL
            }, options.Delay);
        }

        public void AnswerWith(NetworkHost host, IPAddress ip, AnswerOptions options = default)
        {
            var record = AddressRecord.Create(host.LocalDomainName, ip);
            record.TTL = host[ip].TTL ?? options.HostTTL;

            AnswerWith(record, options.Delay);
        }

        public void AnswerWith(NetworkHost host, NetworkService service, DomainName? instance = default, AnswerOptions options = default)
        {
            instance ??= new([service.InstanceName ?? host.Name, .. service.LocalDomainName.Labels]);

            Response.Answers.Add(new PTRRecord
            {
                Name = service.LocalDomainName,
                DomainName = instance,

                TTL = options.ServiceTTL
            });

            Response.AdditionalRecords.Add(MakeService(host, service, instance, options));
            Response.AdditionalRecords.Add(MakeText(service, instance, options));

            AddAddresses(host, options);

            if (options.Delay > Delay)
            {
                Delay = options.Delay;
            }
        }

        /// <summary>
        /// Answers the records of an advertised service instance that match <paramref name="question"/>:
        /// the shared PTR for a service-type browse -- with SRV, TXT and address records bundled
        /// (RFC 6763 §12.1) -- or, for a targeted query, the instance's own SRV (with its addresses
        /// bundled, §12.2) and TXT. A question that matches none of these names adds nothing.
        /// </summary>
        public void AnswerWith(Question question, NetworkHost host, NetworkService service, DomainName? instance = default, AnswerOptions options = default)
        {
            instance ??= new([service.InstanceName ?? host.Name, .. service.LocalDomainName.Labels]);

            bool any = question.Type is DnsType.ANY;

            if (question.Name == service.LocalDomainName)
            {
                if (any || question.Type is DnsType.PTR)
                    AnswerWith(host, service, instance, options);
            }
            else if (question.Name == instance)
            {
                if (any || question.Type is DnsType.SRV)
                {
                    AnswerWith(MakeService(host, service, instance, options), options.Delay);

                    AddAddresses(host, options); // RFC 6763 §12.2: bundle the target's addresses
                }

                if (any || question.Type is DnsType.TXT)
                    AnswerWith(MakeText(service, instance, options), options.Delay);
            }
        }

        private static SRVRecord MakeService(NetworkHost host, NetworkService service, DomainName instance, AnswerOptions options) => new()
        {
            Name = instance,
            Target = host.LocalDomainName,
            Port = ((IPService)service).Port,
            Priority = service.Priority,
            Weight = service.Weight,

            TTL = options.HostTTL
        };

        private static TXTRecord MakeText(NetworkService service, DomainName instance, AnswerOptions options) => new()
        {
            Name = instance,
            // RFC 6763 §6.1: a service with no attributes still carries a single empty string.
            Strings = service.Properties.Count > 0
                ? [.. service.Properties.Select(pair => $"{pair.Key}={pair.Value}")]
                : [string.Empty],

            TTL = options.HostTTL
        };

        private void AddAddresses(NetworkHost host, AnswerOptions options)
        {
            foreach (IPAddress ip in host.IPAddresses)
            {
                var record = AddressRecord.Create(host.LocalDomainName, ip);
                record.TTL = host[ip].TTL ?? options.HostTTL;

                Response.AdditionalRecords.Add(record);
            }
        }

        /// <summary>
        /// Marks every unique record in the response with the cache-flush bit (RFC 6762 §10.2). Shared
        /// records (PTR) are left untouched. Must not be used for legacy unicast replies.
        /// </summary>
        internal void ApplyCacheFlush()
        {
            foreach (ResourceRecord record in Response.Answers.Concat(Response.AdditionalRecords))
                if (record is not PTRRecord)
                    record.SetCacheFlush();
        }

        public struct AnswerOptions
        {
            // Nullable backing so an explicit zero (a goodbye) is distinct from "unset, use the default".
            private TimeSpan? _hostTTL;
            private TimeSpan? _serviceTTL;

            /// <summary>TTL for advertised host address records (RFC 6762 §10 recommends 120 s for host names).</summary>
            public TimeSpan HostTTL     { readonly get => _hostTTL    ?? TimeSpan.FromSeconds(120);  set => _hostTTL = value; }
            // RFC 6762 §10: shared records (PTRs) get a long TTL, host-specific records (SRV/TXT/A) a short one.
            public TimeSpan ServiceTTL  { readonly get => _serviceTTL ?? TimeSpan.FromMinutes(75);   set => _serviceTTL = value; }

            public TimeSpan Delay { get; set; }

            public AnswerOptions(AdvertiseOptions options, bool delay = false)
            {
                if (options.HostTTL is TimeSpan ttlHost)
                    HostTTL = ttlHost;
                if (options.ServiceTTL is TimeSpan ttlService)
                    ServiceTTL = ttlService;

                if (delay)
                {
                    Delay = options.Timeout;
                }
            }

            public static AnswerOptions Goodbye => new()
            {
                HostTTL     = TimeSpan.Zero,
                ServiceTTL  = TimeSpan.Zero
            };
        }
    }
}
