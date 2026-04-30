using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo
{
    public static class WindowsServiceExt
    {
        public static uint? GetPID(this ServiceController sc)
        {
            int size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();

            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (!QueryServiceStatusEx(
                    sc.ServiceHandle,
                    SC_STATUS_PROCESS_INFO,
                    buffer,
                    (uint)size,
                    out _))

                    throw new Win32Exception();

                var status = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);

                return status.dwProcessId > 0 ? status.dwProcessId : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static Version GetVersion(this ServiceController service)
        {
            var servicePath = service.GetExecutablePath();

            var info = FileVersionInfo.GetVersionInfo(servicePath);

            return Version.Parse(info.FileVersion ?? throw new InvalidDataException());
        }

        public static string GetExecutablePath(this ServiceController service) => GetServiceExecutablePathByName(service.ServiceName);
        public static string GetExecutablePath(this ServiceBase service) => GetServiceExecutablePathByName(service.ServiceName);

        internal static string GetServiceExecutablePathByName(string serviceName)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) throw new Win32Exception();

            try
            {
                IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_CONFIG);
                if (svc == IntPtr.Zero) throw new Win32Exception();

                try
                {
                    QueryServiceConfig(svc, IntPtr.Zero, 0, out int bytesNeeded);

                    IntPtr buf = Marshal.AllocHGlobal(bytesNeeded);
                    try
                    {
                        if (!QueryServiceConfig(svc, buf, bytesNeeded, out _))
                            throw new Win32Exception();

                        var config = Marshal.PtrToStructure<QUERY_SERVICE_CONFIG>(buf);
                        return ExtractExecutablePath(config.BinaryPathName);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }
        }

        private static string ExtractExecutablePath(string binaryPathName)
        {
            binaryPathName = binaryPathName.Trim();

            if (binaryPathName.StartsWith('"'))
            {
                // Quoted: "C:\My App\service.exe" --args
                int closingQuote = binaryPathName.IndexOf('"', 1);
                return closingQuote > 1
                    ? binaryPathName[1..closingQuote]
                    : binaryPathName.TrimStart('"');
            }
            else
            {
                // Unquoted: try to find the end of the executable path by looking for .exe
                int exeIndex = binaryPathName.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIndex >= 0)
                    return binaryPathName[..(exeIndex + 4)];

                // Fallback for non-.exe binaries: cut at first space
                int firstSpace = binaryPathName.IndexOf(' ');
                return firstSpace >= 0
                    ? binaryPathName[..firstSpace]
                    : binaryPathName;
            }
        }

        #region P/Invoke
        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;   // <-- the PID
            public uint dwServiceFlags;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatusEx(
            SafeHandle hService,
            int infoLevel,
            IntPtr lpBuffer,
            uint cbBufSize,
            out uint pcbBytesNeeded);

        private const int SC_STATUS_PROCESS_INFO = 0;

        const uint SC_MANAGER_CONNECT = 0x0001;
        const uint SERVICE_QUERY_CONFIG = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        struct QUERY_SERVICE_CONFIG
        {
            public uint ServiceType;
            public uint StartType;
            public uint ErrorControl;
            [MarshalAs(UnmanagedType.LPWStr)] public string BinaryPathName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoadOrderGroup;
            public uint TagId;
            [MarshalAs(UnmanagedType.LPWStr)] public string Dependencies;
            [MarshalAs(UnmanagedType.LPWStr)] public string ServiceStartName;
            [MarshalAs(UnmanagedType.LPWStr)] public string DisplayName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryServiceConfig(IntPtr hService, IntPtr buffer, int bufSize, out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CloseServiceHandle(IntPtr hSCObject);

        #endregion

    }
}