using MadWizard.Desomnia.Manager.Process;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Service.Actions
{
    class CommandExecutor : Actor
    {
        private const string SYSTEM_PROFILE_PATH = @"C:\WINDOWS\system32\config\systemprofile";

        public required ILogger<CommandExecutor> Logger { protected get; init; }

        [ActionHandler("exec")]
        internal void HandleActionExec(string command, string? arguments, ISession? session = null)
        {
            /**
             * Examples:
             * %SYSTEMROOT% / %WINDIR% = C:\Windows
             * %USERPROFILE% = C:\Users\<username>
             */
            command = Path.GetFullPath(Environment.ExpandEnvironmentVariables(command));

            // Fix the wrong profile path
            if (session != null && command.Contains(SYSTEM_PROFILE_PATH, StringComparison.InvariantCultureIgnoreCase))
                command = command.Replace(SYSTEM_PROFILE_PATH, session.GetProfilePath(), 
                    StringComparison.InvariantCultureIgnoreCase);

            var start = DetermineStartInfo(command, arguments);

            Logger.LogInformation($"Executing: '{start.FileName}' with {start.ArgumentsToQuotedString()} as {session?.ToString() ?? "SYSTEM"}");

            if (session != null)
            {
                session.LaunchProcess(start);
            }
            else
            {
                System.Diagnostics.Process.Start(start);
            }
        }

        private ProcessStartInfo DetermineStartInfo(string command, string? arguments)
        {
            var startInfo = new ProcessStartInfo()
            {
                UseShellExecute = false,
            };

            if (File.Exists(command))
            {
                startInfo.WorkingDirectory = Path.GetDirectoryName(command);

                switch (Path.GetExtension(command).ToLowerInvariant())
                {
                    case ".exe":
                    case ".com":
                        startInfo.FileName = command;
                        startInfo.Arguments = arguments;
                        break;

                    case ".bat":
                    case ".cmd":
                        startInfo.FileName = "cmd.exe";
                        startInfo.AddArguments("/C", command);
                        if (arguments != null)
                            startInfo.AddArguments(arguments);
                        startInfo.CreateNoWindow = true;
                        break;
                    case ".ps1":
                        startInfo.FileName = "powershell.exe";
                        startInfo.AddArguments("-ExecutionPolicy", "Bypass");
                        startInfo.AddArguments("-File", command);
                        if (arguments != null)
                            startInfo.AddArguments(arguments);
                        startInfo.CreateNoWindow = true;
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported file type '{Path.GetExtension(command)}'");
                }
            }
            else
                throw new FileNotFoundException($"File not found: {command}", command);

            return startInfo;
        }
    }

    file class ImpersonationContext : IDisposable
    {
        private nint _userToken = 0;
        private bool _isImpersonating = false;

        public ImpersonationContext(nint token)
        {
            _userToken = token;

            if (!ImpersonateLoggedOnUser(_userToken))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            _isImpersonating = true;
        }

        public void Dispose()
        {
            if (_isImpersonating)
            {
                RevertToSelf();

                _isImpersonating = false;
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool RevertToSelf();

    }

    file static class UserProfileHelper
    {
        public static string? GetProfilePath(this ISession session)
        {
            if (((TerminalServicesSession)session).SID is string sid)
            {
                using RegistryKey? profileListKey = Registry.LocalMachine!.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);

                if (profileListKey != null)
                {
                    return profileListKey.GetValue("ProfileImagePath") as string;
                }
            }

            return null;
        }
    }
}