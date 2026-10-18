using NetTools;
using System.Net;
using System.Net.Sockets;

namespace Makaretu.Dns
{
    /// <summary>
    /// Helpers for the multicast-DNS reinterpretation of the 16-bit CLASS field.
    /// </summary>
    /// <remarks>
    /// In multicast DNS (RFC 6762 §5.4 / §10.2) the top bit of the CLASS value is
    /// repurposed: on a question it is the <em>QU</em> ("unicast response requested")
    /// bit, on a resource record it is the cache-flush bit. The version of
    /// Makaretu.Dns.New in use does not expose this bit, and leaves it set in
    /// <see cref="DnsClass"/>, which makes the value compare unequal to e.g.
    /// <see cref="DnsClass.IN"/>. These helpers read the bit and recover the plain class.
    /// </remarks>
    internal static class MakaretuDnsExt
    {
        /// <summary>The top bit of the CLASS field (QU on questions, cache-flush on records).</summary>
        private const ushort MulticastFlag = 0x8000;

        /// <summary>
        /// The DNS-SD service-type enumeration meta-query name, <c>_services._dns-sd._udp.local</c>
        /// (RFC 6763 §9). A PTR query for this name asks which service <em>types</em> exist on the link;
        /// each answer is a PTR whose RDATA is one offered service type (e.g. <c>_ssh._tcp.local</c>).
        /// </summary>
        public static readonly DomainName ServiceEnumeration = new("_services", "_dns-sd", "_udp", "local");

        extension(Message message)
        {
            public IEnumerable<EdnsOption> Options => message.AdditionalRecords.OfType<OPTRecord>().SelectMany(opt => opt.Options);
        }

        extension(ResourceRecord record)
        {
            /// <summary>
            /// Sets the mDNS cache-flush bit on this record's CLASS (RFC 6762 §10.2). Only valid on
            /// <em>unique</em> records (A/AAAA/SRV/TXT, reverse-mapping PTRs); shared records
            /// (service PTRs) must never carry it.
            /// </summary>
            public void SetCacheFlush() => record.Class = (DnsClass)((ushort)record.Class | MulticastFlag);

            /// <summary>
            /// Whether two records describe the same name, type and RDATA (ignoring TTL and the
            /// cache-flush bit). Used for known-answer suppression and duplicate detection.
            /// </summary>
            public bool IsSameRecord(ResourceRecord other)
            {
                if (record.GetType() != other.GetType() || record.Type != other.Type || record.Name != other.Name)
                    return false;

                return (record, other) switch
                {
                    (PTRRecord a, PTRRecord b)          => a.DomainName == b.DomainName,
                    (SRVRecord a, SRVRecord b)          => a.Target == b.Target && a.Port == b.Port,
                    (TXTRecord a, TXTRecord b)          => a.Strings.SequenceEqual(b.Strings),
                    (AddressRecord a, AddressRecord b)  => a.Address.Equals(b.Address),

                    _ => true
                };
            }
        }

        extension(Question question)
        {
            /// <summary>
            /// Whether the QU bit is set, i.e. the sender will also accept a direct
            /// <em>unicast</em> response instead of (only) a multicast one.
            /// </summary>
            public bool QU => ((ushort)question.Class & MulticastFlag) != 0;

            /// <summary>
            /// The actual DNS class with the multicast (QU / cache-flush) bit masked off,
            /// e.g. turns the QU-flagged value back into <see cref="DnsClass.IN"/>.
            /// </summary>
            public DnsClass ClassWithoutMulticastFlag => (DnsClass)((ushort)question.Class & ~MulticastFlag);
        }

        private static IPProtocol ToProtocol(string protocolName) => protocolName switch
        {
            "tcp" => IPProtocol.TCP,
            "udp" => IPProtocol.UDP,

            _ => throw new FormatException("Unknown service protocol: " + protocolName),
        };

        extension(PTRRecord ptr)
        {
            public DomainName ServiceDomainName => ptr.Name;

            public string ServiceName => ptr.Name.Labels[0].Replace("_", "");
            public string ProtocolName => ptr.Name.Labels[1].Replace("_", "");

            public IPProtocol Protocol => ToProtocol(ptr.ProtocolName);

            public string InstanceName => ptr.DomainName.Labels[0];

            /// <summary>
            /// Whether this PTR maps an address back to a host name (a name under <c>in-addr.arpa</c> /
            /// <c>ip6.arpa</c>, RFC 1035 §3.5 / RFC 3596 §2.5) rather than a service type to an instance.
            /// </summary>
            public bool IsReverseMapping => ptr.Name.Labels is [.., "in-addr" or "ip6", "arpa"];

            /// <summary>
            /// Whether this PTR points a DNS-SD service type (<c>_svc._tcp|_udp.local</c>) to one of its
            /// instances. Only these describe a proxied service: registrations may also carry reverse
            /// mappings, the service-type enumeration (<c>_services._dns-sd._udp.local</c>) and subtype
            /// pointers (<c>…._sub._svc._tcp.local</c>).
            /// </summary>
            public bool IsServicePointer => ptr.Name.Labels is [['_', ..], "_tcp" or "_udp", "local"];

            /// <summary>The host name label of a reverse-mapping PTR, whose RDATA is <c>host.local</c>.</summary>
            public string HostName => ptr.DomainName.Labels[0];
        }

        extension(IPAddress address)
        {
            /// <summary>
            /// The reverse-mapping name for this address, e.g. 192.168.128.78 → <c>78.128.168.192.in-addr.arpa</c>
            /// or the nibble form under <c>ip6.arpa</c> for IPv6.
            /// </summary>
            public DomainName ArpaDomainName => address.AddressFamily == AddressFamily.InterNetworkV6
                ? new([.. address.GetAddressBytes().Reverse().SelectMany(b => new[] { (b & 0xF).ToString("x"), (b >> 4).ToString("x") }), "ip6", "arpa"])
                : new([.. address.GetAddressBytes().Reverse().Select(b => b.ToString()), "in-addr", "arpa"]);
        }

        extension(SRVRecord srv)
        {
            public DomainName ServiceDomainName => new ([..srv.Name.Labels.Skip(1)]);

            public string ServiceName => srv.Name.Labels[1].Replace("_", "");
            public string ProtocolName => srv.Name.Labels[2].Replace("_", "");

            public IPProtocol Protocol => ToProtocol(srv.ProtocolName);
            public IPService IPPort => new(srv.Protocol, srv.Port);

            public string InstanceName => srv.Name.Labels[0];
            public string HostName => srv.Target.Labels[0];
        }

        extension(AddressRecord adr)
        {
            public string HostName => adr.Name.Labels[0];
        }

        extension(TXTRecord txt)
        {
            public DomainName ServiceDomainName => new(txt.Name.Labels[1], txt.Name.Labels[2], txt.Name.Labels[3]);

            public string ServiceName => txt.Name.Labels[1].Replace("_", "");
            public string InstanceName => txt.Name.Labels[0];

            public IEnumerable<KeyValuePair<string, string>> KeyValuePairs
            {
                get
                {
                    foreach (var str in txt.Strings)
                    {
                        if (str.Contains('='))
                        {
                            var split = str.Split('=');

                            yield return new(split[0].Trim(), split[1].Trim());
                        }
                    }
                }
            }
        }

        #region Wire read/write
        extension(WireReader reader)
        {
            public IPAddress ReadAddress()
            {
                int size = reader.ReadByte(); // begin and end share the same address family

                return reader.ReadIPAddress(size);
            }

            public IPAddressRange ReadAddressRange()
            {
                int size = reader.ReadByte(); // begin and end share the same address family

                return new IPAddressRange(reader.ReadIPAddress(size), reader.ReadIPAddress(size));
            }
        }

        extension(WireWriter writer)
        {
            public void WriteAddress(IPAddress address)
            {
                byte[] bytes = address.GetAddressBytes();

                writer.WriteByte((byte)bytes.Length);
                writer.WriteIPAddress(address);
            }

            public void WriteAddressRange(IPAddressRange range)
            {
                writer.WriteAddress(range.Begin); 
                writer.WriteIPAddress(range.End); // shares the family/length of Begin
            }
        }
        #endregion
    }
}
