using MadWizard.Desomnia.Network.Configuration.Options;
using PacketDotNet;
using System.Net;

namespace MadWizard.Desomnia.Network.Capture;

internal sealed class IPv4Reassembly : IPReassembly
{
    public IPv4Reassembly(ReassemblyOptions options, TimeProvider? clock = null)
        : base(ReassemblyProtocol.IPv4, options, clock) { }

    protected override bool TryReadFragment(EthernetPacket packet, out IPFragment? fragment)
    {
        fragment = null;
        if (!IPFragment.ReadEthernet(packet, out var bytes, out int type, out int ip)) return RejectMalformed("Truncated Ethernet header");
        if (type != 0x0800) return false;
        if (bytes.Length - ip < 20 || bytes[ip] >> 4 != 4) return RejectMalformed("Truncated or invalid IPv4 header");
        int flags = IPFragment.U16(bytes, ip + 6);
        if ((flags & 0x3fff) == 0) return false;
        int header = (bytes[ip] & 15) * 4, total = IPFragment.U16(bytes, ip + 2);
        if (header < 20 || total < header || total > bytes.Length - ip) return RejectMalformed("Invalid IPv4 header or total length");
        byte protocol = bytes[ip + 9];
        var key = new IPFragment.FragmentKey(new IPAddress(bytes.AsSpan(ip + 12, 4)),
            new IPAddress(bytes.AsSpan(ip + 16, 4)), (uint)IPFragment.U16(bytes, ip + 4), protocol);
        fragment = new IPv4Fragment(packet, bytes, ip, ip + header, total - header,
            (flags & 0x1fff) * 8, (flags & 0x2000) != 0, protocol, key);
        return true;
    }
}
