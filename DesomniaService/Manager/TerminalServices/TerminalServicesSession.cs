using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MadWizard.Desomnia.Session.Manager
{
    public partial class TerminalServicesSession : ISession
    {
        public required uint Id { get; init; }

        public string UserName => !WTSInfo.UserName.IsWhiteSpace() ? field = WTSInfo.UserName : field ?? "?";
        public string DomainName => WTSInfo.Domain;
        public string WindowStationName => WTSInfo.WinStationName;

        public NTAccount? UserAccount => string.IsNullOrEmpty(UserName) ? null : new NTAccount(DomainName, UserName);

        public string? SID => (UserAccount?.Translate(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;

        public string? ClientName => QuerySessionInformation<string>(Id, WTS_INFO_CLASS.WTSClientName)?.NullIfWhiteSpace();

        public bool IsConnected => IsConsoleConnected || IsRemoteConnected;
        public bool IsConsoleConnected => WTSGetActiveConsoleSessionId() == Id;
        public bool IsRemoteConnected => WTSInfo.State == TerminalSessionState.Active && ClientName != null;

        //public bool IsUser => Principal.IsInRole(WindowsBuiltInRole.User);
        public bool IsUser => Principal.UserClaims.Any(c => c.Value.Contains(SID_GROUP_USERS));
        //public bool IsAdministrator => Principal.IsInRole(WindowsBuiltInRole.Administrator);
        public bool IsAdministrator => Principal.UserClaims.Any(c => c.Value.Contains(SID_GROUP_ADMINISTRATORS));

        public bool? IsLocked
        {
            get;

            internal set
            {
                if ((field = value) is bool locked)
                {
                    if (locked)
                    {
                        Locked?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        Unlocked?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        public virtual DateTime? LastInputTime // => null; // WTSInfo.LastInputTime; // doesn't work
        {
            get
            {
                if (Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "DesomniaServiceHelper.exe") is string helper)
                {
                    using var process = LaunchProcess(new(helper, "read LastInputTime")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }).NativeProcess;

                    using (process.StandardOutput)
                    {
                        string ticks = process.StandardOutput.ReadToEnd();

                        return new DateTime(long.Parse(ticks));
                    }
                }

                return null;
            }
        }

        public virtual async Task Lock()
        {
            throw new NotImplementedException();
        }
        public async Task ConnectToConsole()
        {
            await Task.Run(() =>
            {
                if (!WTSConnectSession(Id, WTSGetActiveConsoleSessionId(), "", true))
                    throw new Win32Exception();
            });
        }
        public async Task Disconnect()
        {
            await Task.Run(() =>
            {
                if (!WTSDisconnectSession(0, Id, true))
                    throw new Win32Exception();
            });
        }
        public async Task Logoff()
        {
            await Task.Run(() =>
            {
                if (!WTSLogoffSession(0, Id, true))
                    throw new Win32Exception();
            });
        }

        public event EventHandler? Locked;
        public event EventHandler? Unlocked;
        public event EventHandler? Connected; internal void TriggerConnected() => Connected?.Invoke(this, EventArgs.Empty);
        public event EventHandler? Disconnected; internal void TriggerDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

        internal WindowsIdentity Identity => new(Token);
        internal WindowsPrincipal Principal => new(Identity);

        internal MessageBoxResult? SendMessage(string title, string text,
            MessageBoxStyle style = MessageBoxStyle.OK | MessageBoxStyle.IconInformation, 
            bool wait = false, TimeSpan timeout = default)
        {
            if (!WTSSendMessage(
                WTS_CURRENT_SERVER_HANDLE,
                (int)Id,
                title,
                title.Length * 2, // Unicode: size in bytes
                text,
                text.Length * 2, // Unicode: size in bytes
                (int)style,
                (int)timeout.TotalSeconds, // timeout in seconds
                out int response,
                wait // wait for user response or timeout
            )) throw new Win32Exception(Marshal.GetLastWin32Error());

            if (response == 32000) // IDTIMEOUT
                throw new TimeoutException();
            if (response == 32001) // IDASYNC 
                return null;

            return (MessageBoxResult)response;
        }

        public override string ToString()
        {
            var name = ClientName != null ? $@"{ClientName}\{UserName}" : UserName;

            var state = new List<string>([WTSInfo.State.ToString()]);

            if (IsConsoleConnected)
                state.Add("Console");
            if (IsRemoteConnected)
                state.Add("Remote");

            if (IsLocked.HasValue)
            {
                if (IsLocked.Value)
                    state.Add("Locked");
                else
                    state.Add("Unlocked");
            }

            return $"WTSSession[id={Id}, name={name}, state={string.Join('|', state)}]";
        }
    }
}
