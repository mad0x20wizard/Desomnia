using MadWizard.Desomnia.Network.Neighborhood.Services;
using PacketDotNet.Ndp;
using PacketDotNet.Utils;
using PacketDotNet.Utils.Converters;
using System.Net;
using System.Net.NetworkInformation;

namespace PacketDotNet
{
    internal static class PacketExt
    {
        public static PhysicalAddress? FindSourcePhysicalAddress(this Packet? packet)
        {
            if (packet?.Extract<NdpPacket>() is NdpNeighborSolicitationPacket ndpNeighbor)
                if (ndpNeighbor.OptionsCollection?.OfType<NdpLinkLayerAddressOption>().FirstOrDefault() is NdpLinkLayerAddressOption option)
                    if (option.Type == OptionTypes.SourceLinkLayerAddress)
                        return option.LinkLayerAddress;
            if (packet?.Extract<NdpPacket>() is NdpNeighborAdvertisementPacket ndpNeighborAdvert)
                if (ndpNeighborAdvert.OptionsCollection?.OfType<NdpLinkLayerAddressOption>().FirstOrDefault() is NdpLinkLayerAddressOption option)
                    if (option.Type == OptionTypes.TargetLinkLayerAddress)
                        return option.LinkLayerAddress;
            if (packet?.Extract<NdpPacket>() is NdpRouterAdvertisementPacket ndpRouter)
                if (ndpRouter.OptionsCollection?.OfType<NdpLinkLayerAddressOption>().FirstOrDefault() is NdpLinkLayerAddressOption option)
                    if (option.Type == OptionTypes.SourceLinkLayerAddress)
                        return option.LinkLayerAddress;

            if (packet?.Extract<EthernetPacket>() is EthernetPacket ethernet)
                return ethernet.SourceHardwareAddress;

            return null;
        }

        public static PhysicalAddress? FindTargetPhysicalAddress(this Packet? packet)
        {
            if (packet?.Extract<WakeOnLanPacket>() is WakeOnLanPacket wol)
                return wol.DestinationAddress;
            if (packet?.Extract<EthernetPacket>() is EthernetPacket ethernet)
                return ethernet.DestinationHardwareAddress;

            return null;
        }


        public static IPAddress? FindSourceIPAddress(this Packet? packet)
        {
            if (packet?.Extract<ArpPacket>() is ArpPacket arp)
                return arp.SenderProtocolAddress;
            if (packet?.Extract<IPPacket>() is IPPacket ip)
                return ip.SourceAddress;

            return null;
        }


        public static IPAddress? FindTargetIPAddress(this Packet? packet)
        {
            if (packet?.Extract<ArpPacket>() is ArpPacket arp)
                return arp.TargetProtocolAddress;
            if (packet?.Extract<NdpNeighborSolicitationPacket>() is NdpNeighborSolicitationPacket ndp)
                return ndp.TargetAddress;
            if (packet?.Extract<IPPacket>() is IPPacket ip)
                return ip.DestinationAddress;

            return null;
        }

        public static IPPort? FindTargetService(this Packet? packet)
        {
            if (packet?.Extract<TcpPacket>() is TcpPacket tcp)
                return new IPPort(IPProtocol.TCP, tcp.DestinationPort);
            if (packet?.Extract<UdpPacket>() is UdpPacket udp)
                return new IPPort(IPProtocol.UDP, udp.DestinationPort);
            return null;
        }

        public static bool IsIPUnicast(this Packet? packet, out IPAddress source, out IPAddress target)
        {
            source = IPAddress.None;
            target = IPAddress.None;

            if (packet?.Extract<IPPacket>() is IPPacket ip)
            {
                if (ip.Extract<NdpPacket>() is not null) // NDP packets should be treated as multicast
                    return false;
                if (ip.DestinationAddress.Equals(IPAddress.Broadcast))
                    return false;
                if (ip.DestinationAddress.IsIPv6Multicast)
                    return false;

                source = ip.SourceAddress;
                target = ip.DestinationAddress;

                return true;
            }

            return false;
        }

        public static bool IsIPUnicast(this Packet? packet) => IsIPUnicast(packet, out var _, out var _);

        public static bool IsAddressResolution(this Packet? packet)
        {
            if (packet?.Extract<ArpPacket>() is not null)
                return true;
            if (packet?.Extract<NdpPacket>() is not null)
                return true;

            return false;
        }

        public static bool IsMagicPacket(this EthernetPacket packet, out PhysicalAddress? mac)
        {
            mac = null;

            if (packet.Type == EthernetType.WakeOnLan && packet.PayloadPacket is WakeOnLanPacket layer2wol)
            {
                return (mac = layer2wol.DestinationAddress) != null;
            }

            if ((packet.Type == EthernetType.IPv4 || packet.Type == EthernetType.IPv6)
                && packet.PayloadPacket is IPPacket ip)
                if (ip.Protocol == ProtocolType.Udp && ip.PayloadPacket is UdpPacket udp)
                    if (udp.PayloadPacket is WakeOnLanPacket layer3wol)
                        return (mac = layer3wol.DestinationAddress) != null;

            return false;
        }


        public static bool IsMagicPacket(this EthernetPacket packet)
        {
            return IsMagicPacket(packet, out var _);
        }

        public static bool IsUnMagicPacket(this EthernetPacket packet)
        {
            return IsMagicPacket(packet, out var mac) && packet.SourceHardwareAddress.Equals(mac);
        }

        public static EthernetPacket MakeCopy(this EthernetPacket packet)
        {
            if (Packet.ParsePacket(LinkLayers.Ethernet, (byte[])packet.Bytes.Clone()) is EthernetPacket ethernet)
            {
                return ethernet;
            }

            throw new Exception("Could not copy EthernetPacket");
        }

        public static IPv6Packet WithNDPRouterSolicitation(this IPv6Packet packet)
        {
            packet.Protocol = ProtocolType.IcmpV6;
            packet.HopLimit = 0xFF; // required for NDP

            byte[] bytes =
            [
                // ICMPv6 Header //

                0x85, // 133 = Router Solicitation
                0x00, // Code
                
                0x00, 0x00, // Checksum
                0x00, 0x00, 0x00, 0x00, // Reserved
            ];

            var icmpv6 = new IcmpV6Packet(new(bytes), packet);
            EndianBitConverter.Big.CopyBytes(ComputeIcmpv6Checksum(packet.SourceAddress, packet.DestinationAddress, bytes), bytes, IcmpV6Fields.ChecksumPosition);
            packet.PayloadData = bytes;
            packet.PayloadLength = (ushort)bytes.Length;

            return packet;
        }

        public static IPv6Packet WithNDPNeighborSolicitation(this IPv6Packet packet, IPAddress ip, PhysicalAddress? source)
        {
            packet.Protocol = ProtocolType.IcmpV6;
            packet.HopLimit = 0xFF; // required for NDP

            byte[] bytes =
            [
                // ICMPv6 Header //

                0x87, // 135 = Neighbor Solicitation
                0x00, // Code
                
                0x00, 0x00, // Checksum
                0x00, 0x00, 0x00, 0x00, // Reserved

                .. ip.GetAddressBytes(), // Target Address
            ];

            if (source != null)
            {
                bytes =
                [
                    .. bytes,

                    // NDP Option //

                    0x01, // Method = Source Link Layer Address (SLLA)
                    0x01, // Length = 1 (in units of 8 bytes)

                    .. source.GetAddressBytes(), // Link Layer Address
                ];
            }

            //var icmpv6 = new IcmpV6Packet(new(bytes), packet);
            //icmpv6.Checksum = icmpv6.CalculateIcmpChecksum();
            //packet.PayloadPacket = icmpv6;

            var icmpv6 = new IcmpV6Packet(new(bytes), packet);
            EndianBitConverter.Big.CopyBytes(ComputeIcmpv6Checksum(packet.SourceAddress, packet.DestinationAddress, bytes), bytes, IcmpV6Fields.ChecksumPosition);
            packet.PayloadData = bytes;
            packet.PayloadLength = (ushort)bytes.Length;

            return packet;
        }

        public static IPv6Packet WithNDPNeighborAdvertisement(this IPv6Packet packet, NDPFlags flags, IPAddress ip, PhysicalAddress? target)
        {
            packet.Protocol = ProtocolType.IcmpV6;
            packet.HopLimit = 0xFF; // required for NDP

            byte[] bytes =
            [
                // ICMPv6 Header //

                0x88, // 136 = Neighbor Solicitation
                0x00, // Code
                
                0x00, 0x00, // Checksum
                (byte)flags, // Flags
                0x00, 0x00, 0x00, // Reserved

                .. ip.GetAddressBytes(), // Target Address
            ];

            if (target != null)
            {
                bytes =
                [
                    .. bytes,

                    // NDP Option //

                    0x02, // Method = Target Link-Layer Address (TLLA) 
                    0x01, // Length = 1 (in units of 8 bytes)

                    .. target.GetAddressBytes(), // Link Layer Address
                ];
            }

            var icmpv6 = new IcmpV6Packet(new(bytes), packet);
            EndianBitConverter.Big.CopyBytes(ComputeIcmpv6Checksum(packet.SourceAddress, packet.DestinationAddress, bytes), bytes, IcmpV6Fields.ChecksumPosition);
            packet.PayloadData = bytes;
            packet.PayloadLength = (ushort)bytes.Length;

            return packet;
        }

        public static ushort ComputeIcmpv6Checksum(
            IPAddress srcAddr, IPAddress dstAddr,
            byte[] icmpPayload)
        {
            var srcAddrBytes = srcAddr.GetAddressBytes();
            var dstAddrBytes = dstAddr.GetAddressBytes();

            if (srcAddrBytes.Length != 16 || dstAddrBytes.Length != 16)
                throw new ArgumentException("IPv6 addresses must be 16 bytes");

            int totalLen = 40 + icmpPayload.Length;
            byte[] pseudoAndPayload = new byte[totalLen];

            // MakeCopy IPv6 source and destination addresses
            Array.Copy(srcAddrBytes, 0, pseudoAndPayload, 0, 16);
            Array.Copy(dstAddrBytes, 0, pseudoAndPayload, 16, 16);

            // Add upper-layer packet length (4 bytes)
            int payloadLen = icmpPayload.Length;
            pseudoAndPayload[32] = (byte)((payloadLen >> 24) & 0xFF);
            pseudoAndPayload[33] = (byte)((payloadLen >> 16) & 0xFF);
            pseudoAndPayload[34] = (byte)((payloadLen >> 8) & 0xFF);
            pseudoAndPayload[35] = (byte)(payloadLen & 0xFF);

            // Zero (3 bytes), Next Header (1 byte = 0x3A for ICMPv6)
            pseudoAndPayload[36] = 0;
            pseudoAndPayload[37] = 0;
            pseudoAndPayload[38] = 0;
            pseudoAndPayload[39] = 0x3A;

            // Append the actual ICMPv6 message
            Array.Copy(icmpPayload, 0, pseudoAndPayload, 40, icmpPayload.Length);

            return ComputeChecksum(pseudoAndPayload);
        }

        // Standard Internet checksum computation (RFC 1071)
        private static ushort ComputeChecksum(byte[] data)
        {
            uint sum = 0;
            int i = 0;

            while (i < data.Length - 1)
            {
                ushort word = (ushort)((data[i] << 8) + data[i + 1]);
                sum += word;
                i += 2;
            }

            if (i < data.Length) // handle odd byte
            {
                ushort word = (ushort)(data[i] << 8);
                sum += word;
            }

            // Fold 32-bit sum to 16 bits
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            return (ushort)~sum;
        }

        public static string ToTraceString(this Packet packet)
        {
            //return packet.ToString(StringOutputType.Verbose);
            return packet.ToString(StringOutputType.Normal).Replace("][", "]\n\t[").Replace("[Ethernet", "\t[Ethernet");
        }

    }

    public enum NDPFlags : byte
    {
        None = 0,
        Solicited = 0b_0100_0000,
        Override = 0b_0010_0000,
        Router = 0b_1000_0000,
    }

    internal static class ArpPacketExt
    {
        public static bool IsAnnouncement(this ArpPacket arp)
        {
            return arp.Operation == ArpOperation.Request && arp.SenderProtocolAddress.Equals(arp.TargetProtocolAddress);
        }

        public static bool IsGratuitous(this ArpPacket arp)
        {
            return arp.Operation == ArpOperation.Response && arp.SenderProtocolAddress.Equals(arp.TargetProtocolAddress);
        }

        public static bool IsProbe(this ArpPacket arp)
        {
            return arp.Operation == ArpOperation.Request && arp.SenderProtocolAddress.Equals(IPAddress.Any);
        }
    }

    internal static class WakeOnLanPacketExt
    {
        public static WakeOnLanPacket WithPassword(this WakeOnLanPacket wol, byte[] password)
        {
            var bytes = new byte[wol.Bytes.Length + password.Length];
            System.Array.Copy(wol.Bytes, bytes, wol.Bytes.Length);

            return new WakeOnLanPacket(new ByteArraySegment(bytes))
            {
                Password = password
            };
        }
    }
}
