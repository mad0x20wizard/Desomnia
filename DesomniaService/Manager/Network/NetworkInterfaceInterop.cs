using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>Reads administrative state for IP interfaces, including hidden and filter interfaces.</summary>
    internal static partial class NetworkInterfaceInterop
    {
        public static bool TryGetDisabled(Guid interfaceId, out bool disabled)
        {
            disabled = false;
            uint error = ConvertInterfaceGuidToLuid(in interfaceId, out ulong luid);
            if (IsMissing(error)) return false;
            if (error != 0) throw new Win32Exception((int)error);

            var row = new InterfaceRow { InterfaceLuid = luid };
            error = GetIfEntry2(ref row);
            if (IsMissing(error)) return false;
            if (error != 0) throw new Win32Exception((int)error);
            disabled = row.AdminStatus != 1; // NET_IF_ADMIN_STATUS_UP; unrelated to carrier status
            return true;
        }

        // A disabled adapter can lack an IP Helper entry while still existing in CIM.
        private static bool IsMissing(uint error) => error is 2 or 87 or 1168;

        [LibraryImport("iphlpapi.dll")]
        private static partial uint ConvertInterfaceGuidToLuid(in Guid interfaceGuid, out ulong interfaceLuid);

        [LibraryImport("iphlpapi.dll")]
        private static partial uint GetIfEntry2(ref InterfaceRow row);

        // Complete MIB_IF_ROW2 buffer, including the statistics written by GetIfEntry2.
        // https://learn.microsoft.com/windows/win32/api/netioapi/ns-netioapi-mib_if_row2
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct InterfaceRow
        {
            public ulong InterfaceLuid;
            public uint InterfaceIndex;
            public Guid InterfaceGuid;
            public fixed char Alias[257];
            public fixed char Description[257];
            public uint PhysicalAddressLength;
            public fixed byte PhysicalAddress[32];
            public fixed byte PermanentPhysicalAddress[32];
            public uint Mtu;
            public uint Type;
            public uint TunnelType;
            public uint MediaType;
            public uint PhysicalMediumType;
            public uint AccessType;
            public uint DirectionType;
            public byte InterfaceAndOperStatusFlags;
            public uint OperStatus;
            public uint AdminStatus;
            public uint MediaConnectState;
            public Guid NetworkGuid;
            public uint ConnectionType;
            public ulong TransmitLinkSpeed;
            public ulong ReceiveLinkSpeed;
            // InOctets through OutQLen, all ULONG64 (18 counters).
            public fixed ulong Statistics[18];
        }
    }
}
