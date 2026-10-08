using ConcurrentCollections;
using MadWizard.Desomnia.Network.Naming.Messages;
using MadWizard.Desomnia.Network.Naming.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Naming
{
    public interface IMulticastDNSResolver
    {
        void Announce(DNSMessage announcement) { }

        void Resolve(DNSQuery query);

        void Goodbye(DNSMessage goodbye) { }
    }

    /// <summary>
    /// Receives mDNS responses observed on the link. The symmetric counterpart to
    /// <see cref="IMulticastDNSResolver"/> (which answers queries): this consumes the announcements
    /// other hosts make, so a component can learn about advertised records without speaking the wire
    /// protocol itself.
    /// </summary>
    public interface IMulticastDNSListener
    {
        void ProcessResponse(Message message);
    }

    public class MulticastDNSService() : DNSService(MulticastPort, "mdns")
    {
        /// <summary>The well-known multicast DNS port (RFC 6762).</summary>
        internal static readonly ushort     MulticastPort           = 5353;
        /// <summary>The multicast groups mDNS responses are sent to (RFC 6762 §3).</summary>
        internal static readonly IPAddress  MulticastGroupIPv4      = IPAddress.Parse("224.0.0.251");
        internal static readonly IPAddress  MulticastGroupIPv6      = IPAddress.Parse("ff02::fb");

        /// <summary>RFC 6762 §8.3: at least two unsolicited responses, the first pair one second apart.</summary>
        private static readonly int         AnnouncementCount       = 2;
        private static readonly TimeSpan    AnnouncementInterval    = TimeSpan.FromSeconds(1);


        public required ILogger<MulticastDNSService> Logger { private get; init; }

        public required Lazy<IEnumerable<IMulticastDNSResolver>> Resolvers { private get; init; }
        public required Lazy<IEnumerable<IMulticastDNSListener>> Listeners { private get; init; }

        public bool ForceUnicast { private get; set; } = false;

        /// <summary>
        /// Queries we are currently holding back on, each waiting to see whether another
        /// responder on the link answers for the same name before we step in ourselves.
        /// </summary>
        readonly ConcurrentHashSet<DNSQuery> _pendingQueries = [];

        /// <summary>
        /// Announce ourselves on start (RFC 6762 §8.3) so caches learn of the proxy without waiting for
        /// a browse. Decoupled from the startup sequence -- the proxy's presence must not delay it.
        /// </summary>
        public override async Task Startup() => AnnounceServices();

        /// <summary>
        /// Re-announce after the local host resumes from suspend: a sleep proxy we handed off to
        /// deregisters our records silently (no goodbyes), so caches keep whatever they hold until
        /// TTL -- a fresh cache-flush announcement puts us back in charge of them.
        /// </summary>
        public override async Task Resume() => AnnounceServices();

        private void AnnounceServices()
        {
            try
            {
                var announcement = new DNSMessage();

                foreach (var resolver in Resolvers.Value)
                {
                    resolver.Announce(announcement);
                }

                _ = Announce(announcement);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to announce services");
            }
        }

        public override async Task Shutdown(NetworkShutdownReason reason)
        {
            if (reason == NetworkShutdownReason.ApplicationShutdown)
            {
                try
                {
                    var goodbye = new DNSMessage();

                    foreach (var resolver in Resolvers.Value)
                    {
                        resolver.Goodbye(goodbye);
                    }

                    _ = Announce(goodbye);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to send goodbye on shutdown");
                }
            }
        }

        protected override void ProcessResponse(Message message)
        {
            SkipPendingResponses(message); // filter out all pending responses, that already got answered

            foreach (var listener in Listeners.Value)
                listener.ProcessResponse(message);
        }

        private void SkipPendingResponses(Message received)
        {
            foreach (ResourceRecord answer in received.Answers)
            {
                foreach (DNSQuery query in _pendingQueries) lock (query)
                {
                    query.Response.Answers.RemoveAll(record =>
                    {
                        switch (record)
                        {
                            case PTRRecord recordPTR:
                                return answer is PTRRecord answerPTR && recordPTR.DomainName == answerPTR.DomainName;

                            default:
                                return record.Name == answer.Name;
                        }
                    });
                }
            }
        }
        protected override async void ProcessQuery(DNSQuery query)
        {
            if (query.Request.Opcode == MessageOperation.Query)
            {
                foreach (var resolver in Resolvers.Value)
                    resolver.Resolve(query);

                query.SuppressKnownAnswers(); // RFC 6762 §7.1: don't repeat what the querier already knows

                if (query.IsEmpty)
                    return; // nothing left to say

                _pendingQueries.Add(query);

                try
                {
                    await Task.Delay(query.Delay);

                    lock (query) if (query.Response.Answers.Count > 0)
                    {
                        // Legacy resolvers get a conventional unicast reply; multicast responses get the
                        // cache-flush bit so receivers replace rather than merge our unique records.
                        if (query.IsLegacy)
                            query.MakeLegacy();
                        else
                            query.ApplyCacheFlush();

                        if (Logger.IsEnabled(LogLevel.Trace))
                            foreach (var record in query.Response.Answers)
                                Logger.LogTrace("Sending mDNS response: {Record}", record);

                        RespondTo(query);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not send mDNS response");
                }
                finally
                {
                    _pendingQueries.TryRemove(query);
                }
            }
        }

        internal virtual bool ShouldRespondWithMulticast(DNSQuery query)
        {
            // Legacy one-shot resolvers (ephemeral source port) always get a unicast reply (RFC 6762 §6.7).
            if (query.SourcePort != MulticastPort)
                return false;

            // The QU ("unicast response requested") bit is only a preference. As a passive proxy we cannot
            // observe a unicast answer another responder might send, so we always reply via multicast: this
            // lets other responders see our answer and run duplicate suppression, and is explicitly allowed
            // for a responder that has not multicast the record recently (RFC 6762 §5.4).
            return true;
        }

        protected override void RespondWith(DNSQuery query, EthernetPacket packet)
        {
            if (ForceUnicast || ShouldRespondWithMulticast(query))
            {
                if (packet.PayloadPacket is IPPacket ip)
                {
                    (ip.PayloadPacket as UdpPacket)?.DestinationPort = MulticastPort;

                    (ip as IPv4Packet)?.DestinationAddress = MulticastGroupIPv4;
                    (ip as IPv6Packet)?.DestinationAddress = MulticastGroupIPv6;

                    packet.DestinationHardwareAddress = ip.DestinationAddress.DeriveLayer2MulticastAddress();
                }
            }

            base.RespondWith(query, packet);
        }

        /// <summary>
        /// Multicasts a one-shot mDNS query (a DNS-SD browse) for <paramref name="name"/>, prompting
        /// responders on the link to announce the matching records. Their replies arrive through any
        /// registered <see cref="IMulticastDNSListener"/>. Any <paramref name="known"/> are carried
        /// in the answer section so responders can suppress what the querier already knows (RFC 6762 §7.1).
        /// </summary>
        internal void Browse(DomainName name, DnsType type = DnsType.PTR, IEnumerable<ResourceRecord>? known = null)
        {
            var message = new Message { Id = 0 };
            message.Questions.Add(new Question { Name = name, Type = type, Class = DnsClass.IN });

            if (known is not null)
            {
                message.Answers.AddRange(known);
            }

            SendMulticast(message);
        }

        /// <summary>
        /// Multicasts a probe-shaped query for <paramref name="host"/>'s own name carrying the EDNS0
        /// Owner option (draft-cheshire-edns0-owner-option): tells any sleep proxy still holding a
        /// registration for this owner that it is awake again, so the proxy releases *all* its proxied
        /// records at once -- the takedown keys on the option's MAC alone, so the query carries no records.
        /// <paramref name="sequence"/> must differ from the registered epoch for an immediate release
        /// (an equal one is honored only once the registration is a minute old).
        /// </summary>
        public async Task AnnounceOwner(NetworkHost host, byte sequence)
        {
            if (host.PhysicalAddress is not PhysicalAddress primary)
                throw new NotSupportedException($"Host {host.Name} has no MAC address configured.");

            var message = new Message { Id = 0 };

            message.Questions.Add(new Question { Name = host.LocalDomainName, Type = DnsType.ANY, Class = DnsClass.IN });

            message.AdditionalRecords.Add(new OPTRecord
            {
                Options = [new EdnsOwnerOption
                {
                    Sequence = sequence,
                    PrimaryMac = primary,
                    WakeupMac = (host as VirtualNetworkHost)?.PhysicalHost.PhysicalAddress,
                }]
            });

            for (int i = 0; i < AnnouncementCount; i++)
            {
                SendMulticast(message);

                await Task.Delay(AnnouncementInterval);
            }
        }

        /// <summary>
        /// Multicasts an unsolicited response -- a gratuitous announcement (RFC 6762 §8.3) -- carrying
        /// <paramref name="response"/>'s records, so caches on the link learn of them without having to
        /// ask. Unique records (everything but shared PTRs) get the cache-flush bit so receivers replace
        /// rather than merge (§10.2).
        /// </summary>
        internal async Task Announce(DNSMessage announcement)
        {
            if (announcement.IsEmpty)
                return; // nothing to announce

            announcement.ApplyCacheFlush();

            for (int i = 0; i < AnnouncementCount; i++)
            {
                SendMulticast(announcement.Response);

                await Task.Delay(AnnouncementInterval);
            }
        }

        private void SendMulticast(Message message)
        {
            if (!Device.IsCapturing)
                return;
            if (Device.IPv4Address is not IPAddress source)
                return;

            using var scope = Logger.BeginRealmScope("mdns");

            var ip = new IPv4Packet(source, MulticastGroupIPv4)
            {
                TimeToLive = 255,

                PayloadPacket = new UdpPacket(MulticastPort, MulticastPort)
                {
                    PayloadData = message.ToByteArray()
                }
            };

            var packet = new EthernetPacket(Device.PhysicalAddress, MulticastGroupIPv4.DeriveLayer2MulticastAddress(), EthernetType.None)
            {
                PayloadPacket = ip
            };

            Device.SendPacket(packet, true);
        }
    }
}
