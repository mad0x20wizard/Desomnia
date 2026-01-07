using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Session.Manager
{
    using WTS_CONNECTSTATE_CLASS = TerminalSessionState;

    public partial class TerminalServicesManager
    {
        private IEnumerable<uint> EnumerateSessionIDs()
        {
            if (WTSEnumerateSessions(0, 0, 1, out nint sessionInfo, out int count) == 0)
                throw new Win32Exception();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(sessionInfo + (Marshal.SizeOf<WTS_SESSION_INFO>() * i));

                    yield return info.SessionID;
                }
            }
            finally
            {
                WTSFreeMemory(sessionInfo);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int WTSEnumerateSessions(nint hServer, int reserved, int version, out nint sessionInfo, out int count);
        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(nint memory);

        private struct WTS_SESSION_INFO
        {
            public uint SessionID;

            [MarshalAs(UnmanagedType.LPTStr)]
            public string WinStationName;

            public WTS_CONNECTSTATE_CLASS State;
        }
    }

    public partial class TerminalServicesSession : IDisposable
    {
        const int MAX_TOKEN_CREATION_DELAY = 1000;

        const string SID_GROUP_ADMINISTRATORS = "S-1-5-32-544";
        const string SID_GROUP_USERS = "S-1-5-32-545";

        private nint _token;

        private nint Token
        {
            get
            {
                if (_token == 0)
                {
                    var watch = Stopwatch.StartNew();

                    while (watch.ElapsedMilliseconds < MAX_TOKEN_CREATION_DELAY)
                    {
                        if (!WTSQueryUserToken(id, out var token))
                            throw new Win32Exception();

                        //if ((token) != 0)
                            return _token = token;

                        //Thread.Sleep(100);
                    }

                    throw new NotSupportedException("Unable to obtain user token.");
                }

                return _token;
            }
        }

        private WTSINFO WTSInfo => QuerySessionInformation<WTSINFO>(id, WTS_INFO_CLASS.WTSSessionInfo);

        public virtual void Dispose()
        {
            if (_token != 0)
            {
                CloseHandle(_token);

                _token = 0;
            }
        }

        internal static T? QuerySessionInformation<T>(uint sid, WTS_INFO_CLASS info)
        {
            if (!WTSQuerySessionInformation(0, sid, info, out nint buffer, out int bytes))
                throw new Win32Exception();

            if (buffer != 0)
            {
                try
                {
                    switch (typeof(T).Name)
                    {
                        case nameof(String):
                            return (T?)(object?)Marshal.PtrToStringAuto(buffer);

                        case nameof(WTSINFO):
                            return (T?)Marshal.PtrToStructure(buffer, typeof(WTSINFO));

                        default:
                            throw new ArgumentException(typeof(T).Name);
                    }
                }
                finally
                {
                    WTSFreeMemory(buffer);
                }
            }
            else
            {
                return default;
            }
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(nint server, uint sessionId, WTS_INFO_CLASS wtsInfoClass, out nint buffer, out int bytesReturned);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out nint Token);
        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSConnectSession(uint sessionId, uint targetSessionId, string password, bool wait);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSDisconnectSession(nint server, uint sessionId, bool wait);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSLogoffSession(nint server, uint sessionId, bool wait);
        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(nint memory);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(nint handle);

        internal enum WTS_INFO_CLASS
        {
            WTSInitialProgram = 0,
            WTSApplicationName = 1,
            WTSWorkingDirectory = 2,
            WTSOEMId = 3,
            WTSSessionId = 4,
            WTSUserName = 5,
            WTSWinStationName = 6,
            WTSDomainName = 7,
            WTSConnectState = 8,
            WTSClientBuildNumber = 9,
            WTSClientName = 10,
            WTSClientDirectory = 11,
            WTSClientProductId = 12,
            WTSClientHardwareId = 13,
            WTSClientAddress = 14,
            WTSClientDisplay = 15,
            WTSClientProtocolType = 16,
            WTSIdleTime = 17,
            WTSLogonTime = 18,
            WTSIncomingBytes = 19,
            WTSOutgoingBytes = 20,
            WTSIncomingFrames = 21,
            WTSOutgoingFrames = 22,
            WTSClientInfo = 23,
            WTSSessionInfo = 24,
            WTSSessionInfoEx = 25,
            WTSConfigInfo = 26,
            WTSValidationInfo = 27,
            WTSSessionAddressV4 = 28,
            WTSIsRemoteSession = 29
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct WTSINFO
        {
            public WTS_CONNECTSTATE_CLASS State;

            public int SessionId;

            public int IncomingBytes;
            public int OutgoingBytes;
            public int IncomingFrames;
            public int OutgoingFrames;
            public int IncomingCompressedBytes;
            public int OutgoingCompressedBytes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string WinStationName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
            public string Domain;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public string UserName;

            [MarshalAs(UnmanagedType.I8)]
            private long _connectTime;
            [MarshalAs(UnmanagedType.I8)]
            private long _disconnectTime;
            [MarshalAs(UnmanagedType.I8)]
            private long _lastInputTime;
            [MarshalAs(UnmanagedType.I8)]
            private long _logonTime;
            [MarshalAs(UnmanagedType.I8)]
            private long _currentTime;

            public DateTime? ConnectTime => _connectTime.ConvertFileTime();
            public DateTime? DisconnectTime => _disconnectTime.ConvertFileTime();
            public DateTime? LastInputTime => _lastInputTime.ConvertFileTime();
            public DateTime? LogonTime => _logonTime.ConvertFileTime();
            public DateTime? CurrentTime => _currentTime.ConvertFileTime();

        }

        [DllImport("winsta.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WinStationQueryInformationW(
            IntPtr serverHandle,
            int sessionId,
            WINSTATIONINFOCLASS infoClass,
            out LASTINPUTINFO buffer,
            int bufferLength,
            out int returnedLength);

        static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

        enum WINSTATIONINFOCLASS
        {
            WinStationLastInputTime = 14
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint LastInputTime;
        }

        static bool GetLastInputInfoForSession(int sessionId, out LASTINPUTINFO info)
        {
            return WinStationQueryInformationW(
                WTS_CURRENT_SERVER_HANDLE,
                sessionId,
                WINSTATIONINFOCLASS.WinStationLastInputTime,
                out info,
                Marshal.SizeOf<LASTINPUTINFO>(),
                out int returned);
        }

    }

    public static class APIConverters
    {
        public static string? NullIfWhiteSpace(this string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        public static DateTime? ConvertFileTime(this long value) => value != 0 ? DateTime.FromFileTime(value) : null;
    }
}
