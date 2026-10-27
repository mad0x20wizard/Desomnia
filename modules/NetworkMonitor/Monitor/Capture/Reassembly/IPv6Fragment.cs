using PacketDotNet;
using PacketDotNet.Utils;
using System.Buffers.Binary;

namespace MadWizard.Desomnia.Network.Capture;

internal sealed record IPv6Fragment(EthernetPacket Packet, byte[] Bytes, int IPStart, int DataStart,
    int Length, int Offset, bool More, byte Protocol, int PreviousNextHeader, IPFragment.FragmentKey Key)
    : IPFragment(Packet, Bytes, IPStart, DataStart, Length, Offset, More, Protocol, Key)
{
    public override int ECN => (Bytes[IPStart + 1] >> 4) & 3;
    public override int HeaderLength => DataStart - IPStart - 8;
    public override int MaximumPayloadLength => 65535 - (HeaderLength - 40);
    public override bool IsAtomic => Offset == 0 && !More;
    public override bool IsValid => base.IsValid && (Offset != 0 || HasCompleteHeaderChain());

    public override byte GetTransport(out int offset)
    {
        offset = 0;
        byte protocol = Protocol;
        while (protocol is 0 or 43 or 60 or 51)
        {
            if (offset + 2 > Length) throw new FormatException("Truncated IPv6 extension header.");
            int size = protocol == 51 ? (Data[offset + 1] + 2) * 4 : (Data[offset + 1] + 1) * 8;
            if (size < 8 || offset + size > Length) throw new FormatException("Truncated IPv6 extension header.");
            protocol = Data[offset];
            offset += size;
        }
        return protocol;
    }

    private bool HasCompleteHeaderChain()
    {
        // The first fragment must contain the entire upper-layer header (RFC 7112/8200).
        byte protocol;
        int offset;
        try { protocol = GetTransport(out offset); }
        catch (FormatException) { return false; }
        var data = Data[offset..];
        return protocol switch
        {
            44 => false, // Nested Fragment headers.
            6 => data.Length >= 20 && (data[12] >> 4) >= 5 && data.Length >= (data[12] >> 4) * 4,
            17 => data.Length >= 8,
            // Neighbor Discovery must not be fragmented (RFC 6980).
            58 => data.Length >= 8 && data[0] is not (133 or 134 or 135 or 136 or 137 or 148),
            _ => true,
        };
    }

    public override IPPacket CreatePacket(byte[] bytes, int ecn)
    {
        bytes[1] = (byte)((bytes[1] & 0xcf) | (ecn << 4));
        bytes[PreviousNextHeader - IPStart] = Protocol;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), checked((ushort)(bytes.Length - 40)));
        return new IPv6Packet(new ByteArraySegment(bytes));
    }
}