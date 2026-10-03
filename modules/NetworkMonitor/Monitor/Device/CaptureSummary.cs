using PacketDotNet;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    /// <summary>
    /// Metadata used by the demand/accounting hot path. PacketDotNet materializes several packet
    /// properties (notably IP and hardware addresses), so reading those values once avoids their
    /// repeated allocation in every detector and watch.
    /// </summary>
    internal readonly struct CaptureSummary
    {
        public EthernetPacket Ethernet { get; }

        public PhysicalAddress SourcePhysicalAddress { get; }
        public PhysicalAddress TargetPhysicalAddress { get; }

        public IPAddress? SourceAddress { get; }
        public IPAddress? TargetAddress { get; }

        public IPPort? SourceService { get; }
        public IPPort? TargetService { get; }

        public bool IsIPUnicast { get; }

        public bool IsARPAnnouncement { get; }
        public bool IsARPGratuitous { get; }
        public bool IsARPProbe { get; }

        public bool IsDuplicateAddressDetection { get; }

        public CaptureSummary(EthernetPacket packet)
        {
            Ethernet = packet;

            SourcePhysicalAddress = packet.SourceHardwareAddress;

            var ip = packet.Extract<IPPacket>();
            var arp = ip is null ? packet.Extract<ArpPacket>() : null;
            var transport = ip?.Protocol is ProtocolType.Tcp or ProtocolType.Udp
                ? packet.Extract<TransportPacket>()
                : null;
            var ndp = ip?.Protocol == ProtocolType.IcmpV6 ? packet.Extract<NdpPacket>() : null;
            var solicitation = ndp as NdpNeighborSolicitationPacket;
            var wol = packet.Extract<WakeOnLanPacket>();

            IPAddress? ipSource = ip?.SourceAddress;
            IPAddress? ipTarget = ip?.DestinationAddress;

            SourceAddress = arp?.SenderProtocolAddress ?? ipSource;
            TargetAddress = arp?.TargetProtocolAddress ?? solicitation?.TargetAddress ?? ipTarget;

            SourceService = transport is null ? null : IPPort.SourceOf(transport);
            TargetService = transport is null ? null : IPPort.DestinationOf(transport);

            TargetPhysicalAddress = wol?.DestinationAddress ?? packet.DestinationHardwareAddress;

            IsIPUnicast = ipTarget is not null && ndp is null
                && !ipTarget.Equals(IPAddress.Broadcast)
                && !ipTarget.IsIPv6Multicast;

            if (arp is not null)
            {
                IsARPAnnouncement = arp.Operation == ArpOperation.Request && SourceAddress!.Equals(TargetAddress!);
                IsARPGratuitous = arp.Operation == ArpOperation.Response && SourceAddress!.Equals(TargetAddress);
                IsARPProbe = arp.Operation == ArpOperation.Request && SourceAddress!.Equals(IPAddress.Any);

                IsDuplicateAddressDetection = IsARPProbe;
            }
            else if (solicitation is not null)
            {
                IsDuplicateAddressDetection = ipSource!.Equals(IPAddress.IPv6Any);
            }
        }

        /// <summary>
        /// Forwards PacketDotNet's shallow packet-tree lookup without materializing address values
        /// or payload arrays.
        /// </summary>
        public T? Extract<T>() where T : Packet => Ethernet.Extract<T>();
    }
}
