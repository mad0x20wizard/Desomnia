using PacketDotNet;
using System.Buffers.Binary;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture;

/// <summary>Original wire slices; reading a fragment never decodes its transport layer.</summary>
internal abstract record IPFragment(EthernetPacket Packet, byte[] Bytes, int IPStart, int DataStart,
    int Length, int Offset, bool More, byte Protocol, IPFragment.FragmentKey Key)
{
    internal readonly record struct FragmentKey(IPAddress Source, IPAddress Target, uint Identification, byte Protocol, ulong VLAN);
    public ReadOnlySpan<byte> Data => Bytes.AsSpan(DataStart, Length);
    public int WireLength => Bytes.Length;
    public abstract int ECN { get; }
    public abstract int HeaderLength { get; }
    public abstract int MaximumPayloadLength { get; }
    public virtual bool IsAtomic => false;
    public virtual bool IsValid => Length > 0 && (!More || Length % 8 == 0)
        && Offset + Length <= MaximumPayloadLength;

    /// <summary>Locate the upper-layer header within the reassembled payload.</summary>
    public virtual byte GetTransport(out int offset) { offset = 0; return Protocol; }

    /// <summary>Finish a separate, contiguous copy; never modify the captured frame.</summary>
    public abstract IPPacket CreatePacket(byte[] bytes, int ecn);

    internal static bool ReadEthernet(EthernetPacket packet, out byte[] bytes, out int type, out int ip, out ulong vlan)
    {
        bytes = packet.CapturedBytes;
        type = 0;
        ip = 14;
        vlan = 0;
        if (bytes.Length < 14) return false;
        type = U16(bytes, 12);
        for (int tags = 0; type is 0x8100 or 0x88a8; tags++)
        {
            if (tags == 4 || bytes.Length - ip < 4) return false;
            vlan = (vlan << 16) | (uint)(0x8000 | (type == 0x88a8 ? 0x4000 : 0) | (U16(bytes, ip) & 0xfff));
            type = U16(bytes, ip + 2);
            ip += 4;
        }
        return true;
    }

    internal static int U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
}