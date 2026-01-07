using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Service.Helper
{
    internal static class User
    {
        public static long LastInputTimeTicks
        {
            get
            {
                LASTINPUTINFO info = new LASTINPUTINFO() { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

                if (!GetLastInputInfo(ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return (DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime)).Ticks;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        private struct LASTINPUTINFO
        {
            public uint cbSize;

            public uint dwTime;
        }
    }
}
