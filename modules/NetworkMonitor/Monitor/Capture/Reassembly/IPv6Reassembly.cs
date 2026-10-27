using MadWizard.Desomnia.Network.Configuration.Options;
using PacketDotNet;
using System.Buffers.Binary;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture;

internal sealed class IPv6Reassembly : IPReassembly
{
    public IPv6Reassembly(ReassemblyOptions options, TimeProvider? clock = null)
        : base(ReassemblyProtocol.IPv6, options, clock) { }

    protected override bool TryReadFragment(EthernetPacket packet, out IPFragment? fragment)
    {
        fragment = null;
        if (!IPFragment.ReadEthernet(packet, out var bytes, out int type, out int ip)) return RejectMalformed("Truncated Ethernet header");
        if (type != 0x86dd) return false;
        if (bytes.Length - ip < 40 || bytes[ip] >> 4 != 6) return RejectMalformed("Truncated or invalid IPv6 header");
        int total = 40 + IPFragment.U16(bytes, ip + 4);
        if (total > bytes.Length - ip) return RejectMalformed("Truncated IPv6 payload");
        int pos = ip + 40, previous = ip + 6;
        byte next = bytes[previous];
        while (next != 44)
        {
            if (next is not (0 or 43 or 60 or 51)) return false;
            if (pos + 2 > ip + total) return RejectMalformed("Truncated IPv6 extension header");
            int size = next == 51 ? (bytes[pos + 1] + 2) * 4 : (bytes[pos + 1] + 1) * 8;
            if (size < 8 || pos + size > ip + total) return RejectMalformed("Invalid IPv6 extension header length");
            previous = pos;
            next = bytes[pos];
            pos += size;
        }
        if (pos + 8 > ip + total) return RejectMalformed("Truncated IPv6 fragment header");
        int flags = IPFragment.U16(bytes, pos + 2);
        var key = new IPFragment.FragmentKey(new IPAddress(bytes.AsSpan(ip + 8, 16)),
            new IPAddress(bytes.AsSpan(ip + 24, 16)), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 4, 4)), 0);
        fragment = new IPv6Fragment(packet, bytes, ip, pos + 8, ip + total - pos - 8,
            flags & 0xfff8, (flags & 1) != 0, bytes[pos], previous, key);
        return true;
    }
}
