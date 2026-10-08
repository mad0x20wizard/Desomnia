using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.LaunchDaemon.Native
{
    internal static partial class NetworkInterfaceState
    {
        const string LibSystem = "/usr/lib/libSystem.B.dylib";
        const uint IFF_UP = 0x1;

        // Darwin <ifaddrs.h>: 56 bytes on both arm64 and x86_64, with padding after Flags.
        // https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/getifaddrs.3.html
        [StructLayout(LayoutKind.Sequential)]
        private struct InterfaceAddress
        {
            public nint Next;
            public nint Name;
            public uint Flags;
            public nint Address;
            public nint Netmask;
            public nint Destination;
            public nint Data;
        }

        [LibraryImport(LibSystem, SetLastError = true)]
        private static partial int getifaddrs(out nint addresses);

        [LibraryImport(LibSystem)]
        private static partial void freeifaddrs(nint addresses);

        internal static bool IsDisabled(string name)
        {
            if (getifaddrs(out var addresses) != 0)
                throw new InvalidOperationException($"Cannot read administrative state of '{name}': getifaddrs failed with errno {Marshal.GetLastPInvokeError()}.");

            try
            {
                return IsDisabled(addresses, name);
            }
            finally
            {
                freeifaddrs(addresses);
            }
        }

        internal static bool IsDisabled(nint addresses, string name)
        {
            for (var current = addresses; current != 0;)
            {
                var address = Marshal.PtrToStructure<InterfaceAddress>(current);

                if (string.Equals(Marshal.PtrToStringUTF8(address.Name), name, StringComparison.Ordinal))
                {
                    // Administrative state is independent of carrier, IFF_RUNNING and IP addresses.
                    // In particular, zero flags (e.g. stf0's "flags=0<>") is a valid disabled state.
                    return (address.Flags & IFF_UP) == 0;
                }

                current = address.Next;
            }

            throw new InvalidOperationException($"Cannot read administrative state of '{name}': interface is no longer present.");
        }
    }
}
