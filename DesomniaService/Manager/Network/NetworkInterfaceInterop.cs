using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>Discovers IP-bound adapters and reads their administrative state.</summary>
    internal static partial class NetworkInterfaceInterop
    {
        // Without GAA_FLAG_INCLUDE_ALL_INTERFACES, Windows excludes NDIS filter modules
        // that .NET 9+ also returns from GetAllNetworkInterfaces. Keep CIM discovery
        // separate: disabled adapters and adapters without IP bindings still belong there.
        public static HashSet<Guid> GetIPInterfaceIds()
        {
            uint size = 15000; // recommended initial GetAdaptersAddresses buffer size
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    uint error = GetAdaptersAddresses(0, 0, IntPtr.Zero, buffer, ref size);
                    if (error == 111) continue; // ERROR_BUFFER_OVERFLOW: retry with the new size
                    if (error == 232) return []; // ERROR_NO_DATA
                    if (error != 0) throw new Win32Exception((int)error);

                    HashSet<Guid> ids = [];
                    for (var current = buffer; current != IntPtr.Zero;)
                    {
                        var adapter = Marshal.PtrToStructure<AdapterAddressesHeader>(current);
                        if (Guid.TryParse(Marshal.PtrToStringAnsi(adapter.AdapterName), out var id))
                            ids.Add(id);
                        current = adapter.Next;
                    }
                    return ids;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            // An unstable inventory must not look like every adapter was detached.
            throw new Win32Exception(111);
        }

        [LibraryImport("iphlpapi.dll")]
        private static partial uint GetAdaptersAddresses(uint family, uint flags, IntPtr reserved,
            IntPtr addresses, ref uint size);

        // Only the fixed prefix of IP_ADAPTER_ADDRESSES is needed to walk its linked list.
        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterAddressesHeader
        {
            public ulong Alignment; // union of ULONG Length and DWORD IfIndex
            public IntPtr Next;
            public IntPtr AdapterName;
        }

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
