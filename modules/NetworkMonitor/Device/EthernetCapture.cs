using MadWizard.Desomnia.Network.Filter;
using PacketDotNet;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Device
{
    public struct EthernetCapture
    {
        internal EthernetPacket Packet { get; }

        public PhysicalAddress SourcePhysicalAddress { get; }
        public PhysicalAddress TargetPhysicalAddress { get; }

        public IPAddress? SourceAddress { get; }
        public IPAddress? TargetAddress { get; }

        public IPService? SourcePort { get; }
        public IPService? TargetPort { get; }

        public bool IsIPUnicast { get; }

        public bool IsARPAnnouncement { get; }
        public bool IsARPGratuitous { get; }
        public bool IsARPProbe { get; }

        public bool IsDuplicateAddressDetection { get; }

        public IFilterContext Inbound   { get; }
        public IFilterContext Outbound  { get; }

        public EthernetCapture(EthernetPacket packet)
        {
            Packet = packet;

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

            SourcePort = transport is null ? null : IPService.SourceOf(transport);
            TargetPort = transport is null ? null : IPService.DestinationOf(transport);

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

        public bool HasPacket<T>() where T : Packet => HasPacket<T>(out _);

        public bool HasPacket<T>([NotNullWhen(true)] out T? packet) where T : Packet
        {
            return (packet = FindPacket<T>()) != null;
        }

        public T? FindPacket<T>() where T : Packet => Packet.Extract<T>();
    }
}
