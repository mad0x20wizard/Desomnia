using MadWizard.Desomnia.Network.Configuration.Filter;
using MadWizard.Desomnia.Network.Handoff.Registration;
using MadWizard.Desomnia.Network.Naming.Options;
using MadWizard.Desomnia.Network.Neighborhood.Options;
using Makaretu.Dns;

namespace MadWizard.Desomnia.Network.SleepProxy.Registration
{
    internal static class SleepProxyRegistrationFormat
    {
        private static readonly DomainName LocalZone = new("local");

        // Fallback TTLs when a service has no advertise TTL configured (RFC 6762 §10).
        private static readonly TimeSpan HostRecordTTL = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan ServiceRecordTTL = TimeSpan.FromMinutes(75);

        #region Read from DNS update
        internal static SleepProxyRegistration ParseUpdateMessage(Message message)
        {
            // Zone section (RFC 2136): when present, it must name the ".local" zone. Apple's client
            // sends its registrations without any zone section, so a missing one is accepted.
            if (message.Questions.FirstOrDefault(question => question.Type == DnsType.SOA) is Question zone && zone.Name != LocalZone)
                throw new FormatException($"DNS UPDATE for unsupported zone '{zone.Name}'; only '{LocalZone}' is served");

            if (message.Options.OfType<EdnsOwnerOption>().FirstOrDefault() is not EdnsOwnerOption owner)
                throw new FormatException("DNS UPDATE without an EDNS0 Owner option");
            if (message.Options.OfType<EdnsLeaseOption>().FirstOrDefault() is not EdnsLeaseOption lease)
                throw new FormatException("DNS UPDATE without an EDNS0 Lease option");

            string hostname = DetermineHostname(message.AuthorityRecords);

            var services = ReadServices(message.AuthorityRecords, message.Options).ToList();

            // The instance labels may differ per service (DNS-SD allows it, and Apple devices use
            // derived names for some services): the host is named after the most common label,
            // falling back to its host name; only the deviating services keep their own instance name.
            string name = services
                .GroupBy(service => service.InstanceName!)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault() ?? hostname;

            foreach (var service in services)
                if (service.InstanceName == name)
                    service.InstanceName = null;

            var reg = new SleepProxyRegistration(name, hostname, owner, lease);

            // read IP addresses
            foreach (var adr in message.AuthorityRecords.OfType<AddressRecord>())
                reg.IPAddresses[adr.Address] = new(IPAddressFlags.Static)
                {
                    TTL = adr.TTL,
                };

            if (reg.IPAddresses.Count == 0)
                throw new FormatException("DNS UPDATE without any address record");

            reg.Services.AddRange(services);

            return reg;
        }

        /// <summary>
        /// The host name all address-bearing records (A/AAAA, SRV targets, reverse PTRs) must agree
        /// on -- unlike the service instance labels, a registration has exactly one host name.
        /// </summary>
        static string DetermineHostname(IEnumerable<ResourceRecord> records)
        {
            static void Validate(ref string name, string str)
            {
                if (name == string.Empty)
                    name = str;

                else if (name != str)
                {
                    throw new FormatException($"'{name}' != '{str}'");
                }
            }

            var hostname = string.Empty;
            foreach (var record in records)
            {
                switch (record)
                {
                    case PTRRecord ptr when ptr.IsReverseMapping:
                        Validate(ref hostname, ptr.HostName);
                        break;

                    case SRVRecord srv:
                        Validate(ref hostname, srv.HostName);
                        break;

                    case AddressRecord adr:
                        Validate(ref hostname, adr.HostName);
                        break;
                }
            }

            if (hostname == string.Empty)
                throw new FormatException("Could not determine host name");

            return hostname;
        }

        internal static HandoffServiceInfo ParsePTR(PTRRecord ptr)
        {
            string serviceName = ptr.ServiceName;

            return new()
            {
                Name = serviceName, // TODO: Derive better service name?
                ServiceName = serviceName,
                InstanceName = ptr.InstanceName,

                Protocol = ptr.Protocol,

                AdvertiseServiceTTL = ptr.TTL,
            };
        }

        static IEnumerable<HandoffServiceInfo> ReadServices(IEnumerable<ResourceRecord> records, IEnumerable<EdnsOption> options)
        {
            // Keyed by the full instance name, so multiple instances of the same type stay apart.
            Dictionary<DomainName, HandoffServiceInfo> services = [];

            foreach (var ptr in records.OfType<PTRRecord>().Where(ptr => ptr.IsServicePointer))
                services[ptr.DomainName] = ParsePTR(ptr);

            // SRV/TXT records without a matching service PTR are skipped rather than rejected:
            // Apple's client registers e.g. a "<name>._device-info._tcp.local" TXT with no PTR.
            foreach (var record in records)
            {
                switch (record)
                {
                    case SRVRecord srv when services.TryGetValue(srv.Name, out var service):
                        if (service.Protocol != srv.Protocol)
                            throw new FormatException($"Protocol mismatch @ '{srv.ServiceName}': {service.Protocol} != {srv.Protocol}");
                        service.Port = srv.Port;
                        service.Priority = srv.Priority;
                        service.Weight = srv.Weight;
                        service.AdvertiseHostTTL = srv.TTL;
                        break;

                    case TXTRecord txt when services.TryGetValue(txt.Name, out var service):
                        // Every TXT attribute is kept verbatim to be re-advertised; the friendly
                        // service name is not among them -- it travels in an EdnsServiceMetaOption.
                        foreach (var pair in txt.KeyValuePairs)
                            service.Properties[pair.Key] = pair.Value;
                        break;
                }
            }

            foreach (var option in options.OfType<EdnsServiceOption>())
            {
                var service = services.Values.FirstOrDefault(s => s.Service.LocalDomainName == option.ServiceDomainName)
                    ?? throw new FormatException($"Missing PTR record for service '{option.ServiceDomainName}'");

                ApplyServiceOption(service, option);
            }

            return services.Values;
        }

        private static void ApplyServiceOption(HandoffServiceInfo service, EdnsServiceOption serviceOption)
        {
            switch (serviceOption)
            {
                // optional: add more serviceOption types

                case EdnsServiceInfoOption option:
                    service.Name = option.Name;
                    break;

                case EdnsServiceFilterOption option:
                    foreach (var filter in option.Filters)
                    {
                        switch (filter)
                        {
                            case StaticHostFilterEntry entry:
                                service.HostFilterRule.Add(new HostFilterRuleInfo(entry.Address)            { Type = entry.Type }); break;
                            case DynamicHostFilterEntry entry:
                                service.HostFilterRule.Add(new HostFilterRuleInfo(entry.Name)               { Type = entry.Type }); break;

                            case StaticRangeFilterEntry entry:
                                service.HostRangeFilterRule.Add(new HostRangeFilterRuleInfo(entry.Range)    { Type = entry.Type }); break;
                            case LocalRangeFilterEntry entry:
                                service.HostRangeFilterRule.Add(new LocalRangeFilterRuleInfo()              { Type = entry.Type }); break;
                        }
                    }
                    
                    break;
            }
        }
        #endregion

        #region Write to DNS update message
        internal static Message BuildUpdateMessage(SleepProxyRegistration reg)
        {
            var message = new Message
            {
                Id = (ushort)Random.Shared.Next(1, ushort.MaxValue),
                Opcode = MessageOperation.Update,
            };

            // Zone section (RFC 2136): the ".local" zone.
            message.Questions.Add(new Question { Name = LocalZone, Type = DnsType.SOA, Class = DnsClass.IN });

            DomainName host = new([reg.Hostname, .. LocalZone.Labels]);         // host.local

            // Address records (A / AAAA), each with its reverse-mapping PTR. Apple's SPS only
            // proxies address resolution (ARP/NDP) for addresses it learned from a reverse PTR;
            // the A/AAAA record alone is advertised but never defended on the link.
            // Unique records (everything but the shared PTRs) carry the unique-RRSet bit,
            // like Apple's client sets it: the SPS registers them as unique -- conflict-checked
            // (a hijacked name wakes us) and answered with cache-flush.
            foreach (var (ip, options) in reg.IPAddresses)
            {
                var record = AddressRecord.Create(host, ip);
                record.TTL = options.TTL ?? HostRecordTTL;
                record.SetCacheFlush();
                message.AuthorityRecords.Add(record);

                var reverse = new PTRRecord
                {
                    Name = ip.ArpaDomainName,
                    DomainName = host,
                    TTL = options.TTL ?? HostRecordTTL,
                };
                reverse.SetCacheFlush();
                message.AuthorityRecords.Add(reverse);
            }

            // Service records: PTR (service type -> instance) + SRV (instance -> host:port) + TXT.
            foreach (var info in reg.Services)
            {
                DomainName serviceType = info.Service.LocalDomainName;          // _svc._proto.local
                DomainName instance = new([info.InstanceName ?? reg.Name, .. serviceType.Labels]);   // <name>._svc._proto.local

                message.AuthorityRecords.Add(new PTRRecord
                {
                    Name = serviceType,
                    DomainName = instance,
                    TTL = info.AdvertiseServiceTTL ?? ServiceRecordTTL,
                });

                var srv = new SRVRecord
                {
                    Name = instance,
                    Target = host,
                    Port = info.Port,
                    Priority = info.Priority,
                    Weight = info.Weight,
                    TTL = info.AdvertiseHostTTL ?? HostRecordTTL,
                };
                srv.SetCacheFlush();
                message.AuthorityRecords.Add(srv);

                var txt = new TXTRecord
                {
                    Name = instance,
                    Strings = [.. ExtractServiceProperties(info).Select(entry => $"{entry.Key}={entry.Value}")],
                    TTL = info.AdvertiseHostTTL ?? HostRecordTTL,
                };
                txt.SetCacheFlush();
                message.AuthorityRecords.Add(txt);
            }

            // DNS-SD service-type enumeration (RFC 6763 §9): one shared PTR per advertised type,
            // so the proxy can also answer "which service types exist" browses on our behalf.
            foreach (var serviceType in reg.Services.Select(info => info.Service.LocalDomainName).Distinct())
            {
                message.AuthorityRecords.Add(new PTRRecord
                {
                    Name = MakaretuDnsExt.ServiceEnumeration,
                    DomainName = serviceType,
                    TTL = ServiceRecordTTL,
                });
            }

            // OPT with the Owner option (how to wake us), the requested lease and the filter rules.
            // The paging option (1 / 1) tells the receiver right away that no more messages follow;
            // the MTU splitter re-stamps it when the registration has to travel as a burst.
            message.AdditionalRecords.Add(new OPTRecord()
            {
                Options = [.. reg.Options, new EdnsPagingOption()]
            });

            return message;
        }

        // The TXT record carries the service's own attributes only; the friendly service name travels
        // in an EdnsServiceMetaOption, so a third-party proxy doesn't re-advertise it on the link.
        public static IEnumerable<KeyValuePair<string, string>> ExtractServiceProperties(HandoffServiceInfo service) => service.Properties;
        #endregion
    }
}
