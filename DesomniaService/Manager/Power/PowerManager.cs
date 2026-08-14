using Autofac;
using MadWizard.Desomnia.Power.Source;
using MadWizard.Desomnia.Service;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Power.Manager
{
    // see: https://learn.microsoft.com/en-us/windows/win32/power/system-power-states

    public partial class PowerManager(WindowsService? service = null) : PollingPowerSource, IPowerManager, IPowerSource, IStartable, IDisposable
    {
        public required ILogger<PowerManager> Logger { private get; init; }

        public event EventHandler? Suspended;
        public event EventHandler? ResumeSuspended;

        private bool _hasShutdownPrivilege = false;

        #region Power Status lifecycle
        void IStartable.Start()
        {
            service?.PowerStatusChanged += PowerStatusChanged;
        }

        private void PowerStatusChanged(object? sender, PowerBroadcastStatus status)
        {
            Logger.LogDebug($"{status}");

            switch (status)
            {
                case PowerBroadcastStatus.Suspend:
                    Suspended?.Invoke(this, EventArgs.Empty);
                    break;

                case PowerBroadcastStatus.ResumeSuspend:
                    ResumeSuspended?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        void IDisposable.Dispose()
        {
            service?.PowerStatusChanged -= PowerStatusChanged;
        }
        #endregion

        public override PowerSource Source
        {
            get
            {
                if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                    return PowerSource.Unknown;

                return status.ACLineStatus switch
                {
                    0 => PowerSource.Battery,
                    1 => PowerSource.AC,

                    _ => PowerSource.Unknown, // 255 = unknown status
                };
            }
        }

        async Task IPowerManager.Hibernate()    => await Suspend(true);
        async Task IPowerManager.Suspend()      => await Suspend(false);

        async Task Suspend(bool hibernate = false)
        {
            var acpi = hibernate ? "S4 (hibernate)" : "S1-S3 (sleep)";

            if (hibernate ? IsPwrHibernateAllowed() : IsPwrSuspendAllowed())
            {
                Logger.LogDebug("Requested ACPI state: {acpi}", acpi);

                if (!SetSuspendState(hibernate, false, false))
                {
                    throw new Win32Exception();
                }
            }
            else
            {
                Logger.LogWarning("Requested ACPI state: {acpi} [unsupported]", acpi);
            }
        }

        public async Task Shutdown(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            const string acpi = "S5 (shutdown)";

            EnableShutdownPrivilege();

            Logger.LogDebug("Requested ACPI state: {acpi}", acpi);

            uint seconds = (uint)(timeout?.TotalSeconds ?? 0);

            if (!InitiateSystemShutdownExA(null, message, seconds, force, false, 0))
            {
                throw new Win32Exception();
            }
        }

        public async Task Reboot(TimeSpan? timeout = null, string? message = null, bool force = false)
        {
            const string acpi = "S0 (reboot)";

            EnableShutdownPrivilege();

            Logger.LogDebug("Requested ACPI state: {acpi}", acpi);

            uint seconds = (uint)(timeout?.TotalSeconds ?? 0);

            if (!InitiateSystemShutdownExA(null, message, seconds, force, true, 0))
            {
                throw new Win32Exception();
            }
        }

        async Task<IPowerRequest> IPowerManager.CreateRequest(PowerRequestType type, string reason)
        {
            return new PowerRequest(type switch
            {
                PowerRequestType.Display => PowerRequestsType.DisplayRequired,

                _ => PowerRequestsType.SystemRequired,
            }, reason);
        }

        async IAsyncEnumerator<IPowerRequest> IAsyncEnumerable<IPowerRequest>.GetAsyncEnumerator(CancellationToken token)
        {
            Dictionary<PowerRequestsType, List<PowerRequest>> requestsByType = [];

            using Process process = new()
            {
                StartInfo = new()
                {
                    FileName = @"powercfg",
                    Arguments = "-requests",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            if (process.Start())
            {
                using var reader = process.StandardOutput;

                string? line;
                string? requestTypeName = null;

                PowerRequest? request = null;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line = line.TrimEnd()))
                        continue;

                    if (!char.IsWhiteSpace(line[0]) && line.EndsWith(":"))
                    {
                        requestTypeName = line[..^1].Trim(); // Remove colon
                        request = null;
                    }
                    else if (requestTypeName != null && line.StartsWith('[') && line.Contains(']'))
                    {
                        int bracketEnd = line.IndexOf(']');
                        string callerType = line[1..bracketEnd];
                        string name = line[(bracketEnd + 1)..].Trim();

                        request = new PowerRequest(requestTypeName, callerType, name);

                        if (request.Type is PowerRequestsType type)
                        {
                            if (!requestsByType.TryGetValue(type, out var list))
                                requestsByType[type] = list = [];

                            list.Add(request);
                        }
                    }
                    else if (request != null)
                    {
                        request.Reason = line.Trim();
                    }
                }

                await process.WaitForExitAsync();

                if (requestsByType.TryGetValue(PowerRequestsType.SystemRequired, out var systemRequests))
                    foreach (var systemReuqest in systemRequests)
                        yield return systemReuqest;
            }
            else
            {
                throw new Exception("Failed to run powercfg /requests");
            }
        }

        void EnableShutdownPrivilege()
        {
            if (!_hasShutdownPrivilege)
            {
                Logger.LogDebug("Shutdown privilege needed, trying to adjust...");

                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAccess.TOKEN_ADJUST_PRIVILEGES | TokenAccess.TOKEN_QUERY, out nint tokenHandle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out LUID luid))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                TOKEN_PRIVILEGES tp = new()
                {
                    Luid            = luid,
                    Attributes      = SE_PRIVILEGE_ENABLED,
                    PrivilegeCount  = 1,
                };

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, nint.Zero, nint.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                Logger.LogDebug("Shutdown privilege aquired");

                _hasShutdownPrivilege = true;
            }
        }

        #region API: Token privileges
        const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

        [Flags]
        enum TokenAccess : uint
        {
            TOKEN_ADJUST_PRIVILEGES = 0x0020,
            TOKEN_QUERY = 0x0008
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, TokenAccess DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
        #endregion

        #region API: Shutdown
        internal const int EWX_LOGOFF = 0x00000000;
        internal const int EWX_SHUTDOWN = 0x00000001;
        internal const int EWX_REBOOT = 0x00000002;
        internal const int EWX_FORCE = 0x00000004;
        internal const int EWX_POWEROFF = 0x00000008;
        internal const int EWX_FORCEIFHUNG = 0x00000010;

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool ExitWindowsEx(int flg, int rea);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern bool InitiateSystemShutdownExA(string? lpMachineName, string? lpMessage, uint dwTimeout,
            bool bForceAppsClosed,
            bool bRebootAfterShutdown,
            uint dwReason);

        #endregion

        #region API: Standby-Modus
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool IsPwrSuspendAllowed();
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool IsPwrHibernateAllowed();

        [DllImport("powrprof.dll", CharSet = CharSet.Auto, ExactSpelling = true, SetLastError = true)]
        private static extern bool SetSuspendState(bool hiberate, bool force, bool disableWakeEvent);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_CAPABILITIES
        {
            [MarshalAs(UnmanagedType.I1)] public bool PowerButtonPresent;
            [MarshalAs(UnmanagedType.I1)] public bool SleepButtonPresent;
            [MarshalAs(UnmanagedType.I1)] public bool LidPresent;
            [MarshalAs(UnmanagedType.I1)] public bool SystemS1;
            [MarshalAs(UnmanagedType.I1)] public bool SystemS2;
            [MarshalAs(UnmanagedType.I1)] public bool SystemS3;
            [MarshalAs(UnmanagedType.I1)] public bool SystemS4; // Hibernate
            [MarshalAs(UnmanagedType.I1)] public bool SystemS5;
            [MarshalAs(UnmanagedType.I1)] public bool HiberFilePresent;
            [MarshalAs(UnmanagedType.I1)] public bool FullWake;
            [MarshalAs(UnmanagedType.I1)] public bool VideoDimPresent;
            [MarshalAs(UnmanagedType.I1)] public bool ApmPresent;
            [MarshalAs(UnmanagedType.I1)] public bool UpsPresent;
            [MarshalAs(UnmanagedType.I1)] public bool ThermalControl;
            [MarshalAs(UnmanagedType.I1)] public bool ProcessorThrottle;
            public byte ProcessorMinThrottle;
            public byte ProcessorMaxThrottle;
            [MarshalAs(UnmanagedType.I1)] public bool FastSystemS4;
            [MarshalAs(UnmanagedType.I1)] public bool Hiberboot;
            [MarshalAs(UnmanagedType.I1)] public bool WakeAlarmPresent;
            [MarshalAs(UnmanagedType.I1)] public bool AoAc;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] spare2;
            [MarshalAs(UnmanagedType.I1)] public bool DiskSpinDown;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] spare3;
            [MarshalAs(UnmanagedType.I1)] public bool SystemBatteriesPresent;
            [MarshalAs(UnmanagedType.I1)] public bool BatteriesAreShortTerm;
            [MarshalAs(UnmanagedType.Struct)]
            public BATTERY_REPORTING_SCALE BatteryScale0;
            [MarshalAs(UnmanagedType.Struct)]
            public BATTERY_REPORTING_SCALE BatteryScale1;
            [MarshalAs(UnmanagedType.Struct)]
            public BATTERY_REPORTING_SCALE BatteryScale2;
            public SYSTEM_POWER_STATE AcOnLineWake;
            public SYSTEM_POWER_STATE SoftLidWake;
            public SYSTEM_POWER_STATE RtcWake;
            public SYSTEM_POWER_STATE MinDeviceWakeState;
            public SYSTEM_POWER_STATE DefaultLowLatencyWake;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BATTERY_REPORTING_SCALE
        {
            public uint Granularity;
            public uint Capacity;
        }

        private enum SYSTEM_POWER_STATE
        {
            PowerSystemUnspecified = 0,
            PowerSystemWorking = 1,
            PowerSystemSleeping1 = 2,
            PowerSystemSleeping2 = 3,
            PowerSystemSleeping3 = 4,
            PowerSystemHibernate = 5,
            PowerSystemShutdown = 6,
            PowerSystemMaximum = 7
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool GetPwrCapabilities(out SYSTEM_POWER_CAPABILITIES lpSystemPowerCapabilities);

        private SYSTEM_POWER_CAPABILITIES Capabilities
        {
            get
            {
                if (!GetPwrCapabilities(out SYSTEM_POWER_CAPABILITIES capabilities))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return capabilities;
            }
        }
        #endregion

        #region API: Power-Status
        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }
        #endregion
    }
}
