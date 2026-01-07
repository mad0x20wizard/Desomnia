using MadWizard.Desomnia.Session.Manager;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;

namespace MadWizard.Desomnia.Manager.Process
{
    public static class ProcessLauncher
    {
        public static System.Diagnostics.Process CreateProcessInSession(ProcessStartInfo startup, uint sid, string processName = "winlogon")
        {
            uint winlogonPid = (uint)System.Diagnostics.Process.GetProcessesByName(processName).Where(p => (uint)p.SessionId == sid).First().Id;

            nint hProcess = OpenProcess(MAXIMUM_ALLOWED, false, winlogonPid), hProcessToken = 0;

            nint token = 0;
            try
            {
                SECURITY_ATTRIBUTES sa = new();

                // obtain a handle to the access token of the winlogon process
                if (!OpenProcessToken(hProcess, TOKEN_DUPLICATE, ref hProcessToken))
                    throw new Win32Exception();

                // copy the access token of the winlogon process; the newly created token will be a primary token
                if (!DuplicateTokenEx(hProcessToken, MAXIMUM_ALLOWED, ref sa, (int)SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, (int)TOKEN_TYPE.TokenPrimary, ref token))
                    throw new Win32Exception();
            }
            finally
            {
                CloseHandle(hProcess);
                CloseHandle(hProcessToken);
            }

            try
            {
                return CreateProcessInSession(startup, token);
            }
            finally
            {
                CloseHandle(token);
            }
        }

        public static System.Diagnostics.Process CreateProcessInSession(ProcessStartInfo startup, nint token)
        {
            STARTUPINFO si = new STARTUPINFO(startup).OnInteractiveDesktop();

            nint hRead = 0, hWrite = 0;
            if (startup.RedirectStandardOutput || startup.RedirectStandardError)
            {
                SECURITY_ATTRIBUTES sa = new() { bInheritHandle = true };

                // Create pipe
                if (!CreatePipe(out hRead, out hWrite, ref sa, 0))
                    throw new Win32Exception();

                // Ensure the read handle is not inherited
                SetHandleInformation(hRead, HANDLE_FLAGS.INHERIT, 0);

                si.dwFlags = STARTF_USESTDHANDLES;

                if (startup.RedirectStandardOutput)
                    si.hStdOutput = hWrite;
                if (startup.RedirectStandardError)
                    si.hStdError = hWrite;
            }

            if (startup.RedirectStandardInput)
                throw new ArgumentException("Redirecting standard input is not supported in this context.");

            if (!CreateProcessAsUser
            (
                token,                                          // client's access token
                startup.FileNameAsModule(),                     // file to execute
                startup.ArgumentsAsCommandLine(),               // command line
                SECURITY_ATTRIBUTES.EMTPY,                      // pointer to process SECURITY_ATTRIBUTES
                SECURITY_ATTRIBUTES.EMTPY,                      // pointer to thread SECURITY_ATTRIBUTES
                hRead != 0,                                     // handles are not inheritable
                startup.AsCreationFlags(),                      // flags that specify the priority and creation method of the process
                0,                                              // pointer to new environment block 
                startup.WorkingDirectory.NullIfWhiteSpace(),    // name of current directory 
                si,                                             // pointer to STARTUPINFO structure
                out PROCESS_INFORMATION pi                      // receives PROCESS_INFORMATION
            )) throw new Win32Exception();

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);

            var process = System.Diagnostics.Process.GetProcessById((int)pi.dwProcessId);

            if (startup.RedirectStandardOutput)
            {
                CloseHandle(hWrite);                // Close write handle in this process

                process.AddStandardOutput(new StreamReader(new FileStream(new SafeFileHandle(hRead, true), FileAccess.Read)));
            }

            return process;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(nint hSnapshot);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("advapi32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern bool OpenProcessToken(nint ProcessHandle, int DesiredAccess, ref nint TokenHandle);

        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx")]
        private extern static bool DuplicateTokenEx(nint ExistingTokenHandle, uint dwDesiredAccess,
            ref SECURITY_ATTRIBUTES lpThreadAttributes, int TokenType,
            int ImpersonationLevel, ref nint DuplicateTokenHandle);

        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUser", SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        private extern static bool CreateProcessAsUser(nint hToken, string? lpApplicationName, string? lpCommandLine, SECURITY_ATTRIBUTES lpProcessAttributes,
            SECURITY_ATTRIBUTES lpThreadAttributes, bool bInheritHandle, int dwCreationFlags, nint lpEnvironment,
            string? lpCurrentDirectory, in STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr hObject, HANDLE_FLAGS dwMask, HANDLE_FLAGS dwFlags);

        [Flags]
        enum HANDLE_FLAGS : uint
        {
            INHERIT = 1,
            PROTECT_FROM_CLOSE = 2
        }

        private const int TOKEN_DUPLICATE = 0x0002;
        private const uint MAXIMUM_ALLOWED = 0x2000000;

        private const int IDLE_PRIORITY_CLASS = 0x40;
        private const int NORMAL_PRIORITY_CLASS = 0x20;
        private const int HIGH_PRIORITY_CLASS = 0x80;
        private const int REALTIME_PRIORITY_CLASS = 0x100;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public nint hProcess;
            public nint hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb = Marshal.SizeOf<STARTUPINFO>();

            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public nint lpReserved2;
            public nint hStdInput;
            public nint hStdOutput;
            public nint hStdError;

            public STARTUPINFO(ProcessStartInfo startup)
            {

            }

            // By default CreateProcessAsUser creates a process on a non-interactive window station, meaning
            // the window station has a desktop that is invisible and the process is incapable of receiving
            // user input. To remedy this we set the lpDesktop parameter to indicate we want to enable user 
            // interaction with the new process.
            public STARTUPINFO OnInteractiveDesktop()
            {
                this.lpDesktop = @"winsta0\default"; // interactive window station parameter; basically this indicates that the process created can display a GUI on the desktop

                return this;
            }
        }

        const int STARTF_USESTDHANDLES = 0x00000100;

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public static SECURITY_ATTRIBUTES EMTPY = new();

            public int Length = Marshal.SizeOf<SECURITY_ATTRIBUTES>();
            public nint lpSecurityDescriptor;
            public bool bInheritHandle;

            public SECURITY_ATTRIBUTES()
            {

            }
        }

        private enum TOKEN_TYPE : int
        {
            TokenPrimary = 1,
            TokenImpersonation = 2
        }

        private enum SECURITY_IMPERSONATION_LEVEL : int
        {
            SecurityAnonymous = 0,
            SecurityIdentification = 1,
            SecurityImpersonation = 2,
            SecurityDelegation = 3,
        }

        class Handle(nint handle) : IDisposable
        {
            public static implicit operator nint(Handle handle) => handle;
            public static implicit operator Handle(nint handle) => new(handle);

            void IDisposable.Dispose()
            {
                CloseHandle(handle);
            }
        }

    }

    internal static class Arguments
    {
        private const int CREATE_NO_WINDOW      = 0x08000000;
        private const int CREATE_NEW_CONSOLE    = 0x00000010;

        public static int AsCreationFlags(this ProcessStartInfo startup)
        {
            int flags = 0;
            if (startup.CreateNoWindow)
                flags |= CREATE_NO_WINDOW;
            else
                flags |= CREATE_NEW_CONSOLE;
            return flags;
        }

        public static string? FileNameAsModule(this ProcessStartInfo startup)
        {
            if (File.Exists(startup.FileName))
                return startup.FileName;
            else
                return null;
        }

        public static string ArgumentsAsCommandLine(this ProcessStartInfo startup)
        {
            MethodInfo? internalMethod = typeof(ProcessStartInfo)
                .GetMethod("BuildArguments", BindingFlags.NonPublic | BindingFlags.Instance);
            
            string arguments = Path.GetFileName(startup.FileName);
            if (internalMethod != null)
                arguments += " " + internalMethod.Invoke(startup, null) as string;
            else if (startup.ArgumentList.Count > 0)
                arguments += " " + string.Join(' ', startup.ArgumentList);
            else if (startup.Arguments.Length > 0)
                arguments += " " + startup.Arguments;
            return arguments;
        }

        public static string ArgumentsToQuotedString(this ProcessStartInfo start)
        {
            if (start.ArgumentList.Count > 0)
                return "[" + string.Join(", ", start.ArgumentList.Select(a => $"'{a}'")) + "]";
            else
                return "'" + start.Arguments + "'";
        }

        public static void AddArguments(this ProcessStartInfo startInfo, params string[] arguments)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
    }

    internal static class ProcessExtension
    {
        internal static System.Diagnostics.Process AddStandardOutput(this System.Diagnostics.Process proc, StreamReader output)
        {
            // Use reflection to set the private _standardOutput field
            FieldInfo? outputField = typeof(System.Diagnostics.Process).GetField("_standardOutput", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find _standardOutput field. .NET version may be incompatible.");

            outputField.SetValue(proc, output);

            //// Set HasExited to false initially (Process may have exited already, so beware)
            //FieldInfo haveOutputReader = type.GetField("haveOutputReader", BindingFlags.Instance | BindingFlags.NonPublic);
            //haveOutputReader?.SetValue(proc, true);

            return proc;
        }
    }

}
