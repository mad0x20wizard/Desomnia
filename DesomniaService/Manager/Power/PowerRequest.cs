using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Power.Manager
{
    internal class PowerRequest : IPowerRequest
    {
        internal PowerRequest(string type, string callerType, string name, string? reason = null)
        {
            this.Type = type switch
            {
                "DISPLAY"           => PowerRequestsType.DisplayRequired,
                "SYSTEM"            => PowerRequestsType.SystemRequired,
                "AWAYMODE"          => PowerRequestsType.AwayModeRequired,
                "EXECUTION"         => PowerRequestsType.ExecutionRequired,

                _ => null
            };

            this.CallerType = callerType;

            this.Name = name;
            this.Reason = reason;
        }

        internal PowerRequest(PowerRequestsType type, string reason)
        {
            Type = type;

            CallerType = "SERVICE";
            Name = "DesomniaService";

            // Create new power request.
            POWER_REQUEST_CONTEXT context = new()
            {
                Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                Version = POWER_REQUEST_CONTEXT_VERSION,
                SimpleReasonString = Reason = reason
            };

            Handle = PowerCreateRequest(ref context);

            if (Handle == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!PowerSetRequest(Handle, type))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        internal readonly nint Handle;

        internal PowerRequestsType? Type { get; set; }

        internal string CallerType { get; private init; }

        public string Name { get; private init; }
        public string? Reason { get; set; }

        public void Dispose()
        {
            if (Handle != nint.Zero && Type != null)
            {
                if (!PowerClearRequest(Handle, Type.Value))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        #region API: Power-Requests
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint PowerCreateRequest(ref POWER_REQUEST_CONTEXT Context);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerSetRequest(nint PowerRequestHandle, PowerRequestsType RequestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerClearRequest(nint PowerRequestHandle, PowerRequestsType RequestType);


        private const int POWER_REQUEST_CONTEXT_VERSION = 0;
        private const int POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct POWER_REQUEST_CONTEXT
        {
            public uint Version;
            public uint Flags;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string SimpleReasonString;
        }

        #endregion
    }

    internal enum PowerRequestsType
    {
        DisplayRequired = 0,
        SystemRequired,
        AwayModeRequired,
        ExecutionRequired
    }
}
