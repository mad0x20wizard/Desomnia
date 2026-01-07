using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.LaunchDaemon.Tools
{
    internal static class MacOSHelper
    {
        [DllImport("libc")]
        private static extern uint geteuid();

        public static bool IsRoot => geteuid() == 0;
    }
}
