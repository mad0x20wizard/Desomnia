using MadWizard.Desomnia.LaunchDaemon.Native;
using System.Runtime.InteropServices;
using Xunit;

namespace MadWizard.Desomnia.LaunchDaemon.Tests
{
    public class NetworkInterfaceStateTests
    {
        [Theory]
        [InlineData(0u, true)]       // stf0: flags=0<> must not be treated as an unreadable state
        [InlineData(0x1u, false)]   // IFF_UP, without IFF_RUNNING or an address
        [InlineData(0x40u, true)]   // IFF_RUNNING alone does not mean administratively enabled
        [InlineData(0x41u, false)]
        [InlineData(0x8000u, true)] // unrelated flags must not affect administrative state
        [InlineData(0x8001u, false)]
        public void AdministrativeStateDependsOnlyOnUpFlag(uint flags, bool disabled)
        {
            using var entry = new NativeEntry("stf0", flags);

            Assert.Equal(disabled, NetworkInterfaceState.IsDisabled(entry.Pointer, "stf0"));
        }

        [Fact]
        public void FindsTheExactInterfaceBeyondOtherInterfacesAndAddressEntries()
        {
            using var target = new NativeEntry("en1", 0);
            using var alias = new NativeEntry("en0", 1, target.Pointer);
            using var first = new NativeEntry("en0", 1, alias.Pointer);

            Assert.True(NetworkInterfaceState.IsDisabled(first.Pointer, "en1"));
            Assert.False(NetworkInterfaceState.IsDisabled(first.Pointer, "en0"));
            Assert.Throws<InvalidOperationException>(() => NetworkInterfaceState.IsDisabled(first.Pointer, "en"));
        }

        [Fact]
        public void MissingInterfaceIsReportedAsUnknownInsteadOfDisabled()
        {
            using var entry = new NativeEntry("en0", 1);

            var exception = Assert.Throws<InvalidOperationException>(() => NetworkInterfaceState.IsDisabled(entry.Pointer, "stf0"));

            Assert.Contains("stf0", exception.Message);
            Assert.Throws<InvalidOperationException>(() => NetworkInterfaceState.IsDisabled(0, "stf0"));
        }

        [MacOSFact]
        public void NativeLookupReadsTheLoopbackInterface()
        {
            Assert.False(NetworkInterfaceState.IsDisabled("lo0"));
        }

        private sealed class MacOSFactAttribute : FactAttribute
        {
            public MacOSFactAttribute()
            {
                if (!OperatingSystem.IsMacOS())
                    Skip = "Requires macOS libSystem.";
            }
        }

        // Construct Darwin's LP64 layout independently of the production struct. Null address
        // pointers deliberately exercise interfaces with no assigned IP or link-level address.
        private sealed class NativeEntry : IDisposable
        {
            public nint Pointer { get; }
            private readonly nint namePointer;

            public NativeEntry(string name, uint flags, nint next = 0)
            {
                Assert.Equal(8, IntPtr.Size);
                namePointer = Marshal.StringToCoTaskMemUTF8(name);
                Pointer = Marshal.AllocHGlobal(56);
                Marshal.Copy(new byte[56], 0, Pointer, 56);
                Marshal.WriteIntPtr(Pointer, 0, next);
                Marshal.WriteIntPtr(Pointer, 8, namePointer);
                Marshal.WriteInt32(Pointer, 16, unchecked((int)flags));
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(Pointer);
                Marshal.FreeCoTaskMem(namePointer);
            }
        }
    }
}
