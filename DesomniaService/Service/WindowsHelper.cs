using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MadWizard.Desomnia.Service.Windows
{
    public static class WindowsHelper
    {
        public static bool IsWindowsService(this System.Diagnostics.Process process)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            return process.SessionId == 0;
        }

        public static bool HasElevatedPrivileges
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
        }
    }
}