using PacketDotNet;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Capture
{
    public interface INetworkCapture : IIEnumerable<EthernetPacket>
    {
        EthernetType    Type                    { get; }

        PhysicalAddress SourcePhysicalAddress   { get; }
        PhysicalAddress TargetPhysicalAddress   { get; }

        IPEndPoint?     SourceEndPoint          { get; } 
        IPEndPoint?     TargetEndPoint          { get; }

        int             PayloadLength           { get; }
        byte[]          PayloadData             { get; }

        bool            IsIPUnicast
        {
            get
            {
                if (HasPacket<IPPacket>() && !HasPacket<NdpPacket>())
                {
                    var family = this.TargetAddress?.AddressFamily;

                    switch (this.TargetAddress)
                    {
                        case IPAddress ipv4 when family == AddressFamily.InterNetwork:
                            return !ipv4.Equals(IPAddress.Broadcast); // TODO check for IPv4 multicast

                        case IPAddress ipv6 when family == AddressFamily.InterNetworkV6:
                            return !ipv6.IsIPv6Multicast;
                    }
                }

                return false;
            }
        }

        #region Packet access
        /// <summary>
        /// Find a decoded layer of the logical packet. Reassembled captures materialize and
        /// cache a normalized IP packet on demand. Use enumeration for original Ethernet frames;
        /// use Packet.CapturedBytes when forwarding wire data without recalculating headers.
        /// </summary>
        T? FindPacket<T>() where T : Packet;

        /// <summary>
        /// Check presence without requesting a decoded instance. Implementations may answer
        /// from metadata; payload-dependent checks can still require packet materialization.
        /// </summary>
        bool HasPacket<T>() where T : Packet => HasPacket<T>(out _);

        /// <summary>Return the decoded logical packet when present, materializing it if necessary.</summary>
        bool HasPacket<T>([NotNullWhen(true)] out T? packet) where T : Packet
        {
            return (packet = FindPacket<T>()) != null;
        }
        #endregion
    }

    public static class AddressExt
    {
        extension(INetworkCapture capture)
        {
            public IPAddress? SourceAddress => capture.SourceEndPoint?.Address;
            public IPAddress? TargetAddress => capture.TargetEndPoint?.Address;
        }
    }

    public static class AddressResolutionExt
    {
        extension (INetworkCapture capture)
        {
            public bool IsARPAnnouncement => capture.FindPacket<ArpPacket>()    is { Operation: ArpOperation.Request }
                && capture.SourceAddress!.Equals(capture.TargetAddress);
            public bool IsARPGratuitous => capture.FindPacket<ArpPacket>()      is { Operation: ArpOperation.Response }
                && capture.SourceAddress!.Equals(capture.TargetAddress);
            public bool IsARPProbe => capture.FindPacket<ArpPacket>()           is { Operation: ArpOperation.Request }
                && capture.SourceAddress!.Equals(IPAddress.Any);

            public bool IsDuplicateAddressDetection
            {
                get
                {
                    if (capture.IsARPProbe)
                        return true;

                    if (capture.HasPacket<NdpNeighborSolicitationPacket>() && capture.SourceAddress!.Equals(IPAddress.IPv6Any))
                        return true;

                    return false;
                }
            }
        }
    }

    public static class MagicPacketExt
    {
        extension(INetworkCapture capture)
        {
            public bool IsMagicPacket()      => capture.HasPacket<WakeOnLanPacket>();
            public bool IsUnMagicPacket()    => capture.IsMagicPacket(out var address) && capture.SourcePhysicalAddress.Equals(address);

            public bool IsMagicPacket([NotNullWhen(true)] out PhysicalAddress? address)
            {
                return (address = capture.FindPacket<WakeOnLanPacket>()?.DestinationAddress) is not null;
            }
        }
    }

    public static class DebugExt
    {
        extension(INetworkCapture capture)
        {
            public string ToTraceString() => string.Join(Environment.NewLine, capture.Select(p => p.ToTraceString()));
        }
    }
}
