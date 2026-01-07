using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Daemon.Tools
{
    internal static class UnixHelper
    {
        [DllImport("libc")]
        private static extern uint geteuid();

        public static bool IsRoot => geteuid() == 0;
    }
}
