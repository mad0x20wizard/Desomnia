using PacketDotNet;
using PacketDotNet.Utils;
using System.Buffers.Binary;

namespace MadWizard.Desomnia.Network.Capture;

internal sealed record IPv4Fragment(EthernetPacket Packet, byte[] Bytes, int IPStart, int DataStart,
    int Length, int Offset, bool More, byte Protocol, IPFragment.FragmentKey Key)
    : IPFragment(Packet, Bytes, IPStart, DataStart, Length, Offset, More, Protocol, Key)
{
    public override int ECN => Bytes[IPStart + 1] & 3;
    public override int HeaderLength => DataStart - IPStart;
    // Only offset zero determines the original IPv4 header length (options may differ).
    public override int MaximumPayloadLength => 65535 - (Offset == 0 ? HeaderLength : 20);
    public override bool IsValid => base.IsValid && (U16(Bytes, IPStart + 6) & 0xc000) == 0;

    public override IPPacket CreatePacket(byte[] bytes, int ecn)
    {
        bytes[6] = bytes[7] = 0;
        bytes[1] = (byte)((bytes[1] & 0xfc) | ecn);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), checked((ushort)bytes.Length));
        var ip = new IPv4Packet(new ByteArraySegment(bytes));
        ip.UpdateIPChecksum();
        return ip;
    }
}