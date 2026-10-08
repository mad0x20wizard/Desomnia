using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.HyperV.Middleware
{
    /// <summary>An immutable routing snapshot; packet classification allocates no objects.</summary>
    internal sealed class HyperVPacketMap
    {
        internal const int EthernetHeaderLength = 14;
        private readonly ulong _host;
        private readonly FrozenSet<ulong> _virtualMachines;

        internal HyperVPacketMap(PhysicalAddress host, IEnumerable<PhysicalAddress> virtualMachines)
        {
            _host = GetAddress(host);
            _virtualMachines = virtualMachines.Select(GetAddress).Where(address => address != _host).ToFrozenSet();
        }

        internal bool Accept(ReadOnlySpan<byte> packet, bool virtualDevice)
        {
            if (packet.Length < EthernetHeaderLength)
                return false;
            var destination = ReadAddress(packet);
            var source = ReadAddress(packet[6..]);
            var hostToVirtualMachine = source == _host && _virtualMachines.Contains(destination)
                || destination == _host && _virtualMachines.Contains(source);
            return virtualDevice == hostToVirtualMachine;
        }

        internal bool SendViaVirtualDevice(ReadOnlySpan<byte> packet) => packet.Length >= EthernetHeaderLength
            && (packet[0] & 1) == 0 && _virtualMachines.Contains(ReadAddress(packet));

        internal bool AcceptNonVirtualTraffic(ReadOnlySpan<byte> packet) => packet.Length >= EthernetHeaderLength
            && !_virtualMachines.Contains(ReadAddress(packet)) && !_virtualMachines.Contains(ReadAddress(packet[6..]));

        private static ulong GetAddress(PhysicalAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 6 || (bytes[0] & 1) != 0 || ReadAddress(bytes) == 0)
                throw new ArgumentException($"'{address}' is not a unicast Ethernet address.");
            return ReadAddress(bytes);
        }

        private static ulong ReadAddress(ReadOnlySpan<byte> bytes) =>
            ((ulong)BinaryPrimitives.ReadUInt32BigEndian(bytes) << 16) | BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);
    }
}
