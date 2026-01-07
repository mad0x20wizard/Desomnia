using Autofac;
using MadWizard.Desomnia.Process.Manager;
using MadWizard.Desomnia.Service;
using Microsoft.Extensions.Logging;
using System.ServiceProcess;
using static MadWizard.Desomnia.Session.Manager.TerminalServicesSession;

namespace MadWizard.Desomnia.Session.Manager
{
    public partial class TerminalServicesManager : ISessionManager, IDisposable
    {
        private Dictionary<uint, TerminalServicesSession>? _sessions; // lazy init

        public required ILogger<TerminalServicesManager> Logger { protected get; init; }

        public required ProcessManager ProcessManager { protected get; init; }

        public TerminalServicesManager(WindowsService? service = null)
        {
            if (service != null)
            {
                service.SessionChanged += Service_SessionChanged;
            }
        }

        protected Dictionary<uint, TerminalServicesSession> Sessions
        {
            get
            {
                if (_sessions == null )
                {
                    _sessions = EnumerateSessionIDs()
                        .Select(MaybeConfigureSession)
                        .Where(s => s != null).Select(s => s!)
                        .ToDictionary(s => s.Id);

                    Logger.LogDebug($"Enumerating existing user sessions:");

                    foreach (var session in _sessions.Values)
                    {
                        Logger.LogDebug($"{session}");
                    }

                    Logger.LogDebug($"Startup of {GetType().Name} complete.");
                }

                return _sessions;
            }
        }

        public event EventHandler<ISession>? UserLogon;
        public event EventHandler<ISession>? RemoteConnect;
        public event EventHandler<ISession>? ConsoleConnect;
        public event EventHandler<ISession>? RemoteDisconnect;
        public event EventHandler<ISession>? ConsoleDisconnect;
        public event EventHandler<ISession>? UserLogoff;

        public ISession this[uint sid] => Sessions[sid];

        public ISession? ConsoleSession
        {
            get
            {
                var consoleSID = WTSGetActiveConsoleSessionId();

                return Sessions.TryGetValue(consoleSID, out var session) ? session : null;
            }

            set
            {
                if (value != null)
                {
                    ((TerminalServicesSession)value).ConnectToConsole().Wait();
                }
                else
                {
                    this.ConsoleSession?.Disconnect().Wait();
                }
            }
        }

        public ISession? FindSessionByID(uint sid)
        {
            return Sessions.TryGetValue(sid, out var session) ? session : null;
        }
        public ISession? FindSessionByUserName(string user)
        {
            return Sessions.Values.Where(s => s.UserName.Equals(user, StringComparison.InvariantCultureIgnoreCase)).SingleOrDefault();
        }

        public IEnumerator<ISession> GetEnumerator()
        {
            return Sessions.Values.GetEnumerator();
        }

        private async void Service_SessionChanged(object? sender, SessionChangeDescription desc)
        {
            uint sid = (uint)desc.SessionId;

            Sessions.TryGetValue(sid, out var session);

            if (session != null)
            {
                Logger.LogDebug($"{session} -> {desc.Reason}");

                switch (desc.Reason)
                {
                    case SessionChangeReason.SessionLogoff:
                        if (Sessions.Remove(sid))
                        {
                            UserLogoff?.Invoke(this, session);

                            session.Dispose();
                        }
                        break;

                    case SessionChangeReason.RemoteConnect:
                        session.TriggerConnected();
                        RemoteConnect?.Invoke(this, session);
                        break;
                    case SessionChangeReason.ConsoleConnect:
                        session.TriggerConnected();
                        ConsoleConnect?.Invoke(this, session);
                        break;

                    case SessionChangeReason.RemoteDisconnect:
                        session.TriggerDisconnected();
                        RemoteDisconnect?.Invoke(this, session);
                        break;
                    case SessionChangeReason.ConsoleDisconnect:
                        session.TriggerDisconnected();
                        ConsoleDisconnect?.Invoke(this, session);
                        break;

                    case SessionChangeReason.SessionLock:
                        session.IsLocked = true;
                        break;
                    case SessionChangeReason.SessionUnlock:
                        session.IsLocked = false;
                        break;
                }
            }
            else
            {
                switch (desc.Reason)
                {
                    case SessionChangeReason.SessionLogon when MaybeConfigureSession(sid) is TerminalServicesSession configured:
                        Sessions[sid] = configured;
                        UserLogon?.Invoke(this, configured);

                        Logger.LogDebug($"{configured} -> {desc.Reason}");
                        break;

                    default:
                        Logger.LogDebug($"WTSSession[id={desc.SessionId}, name=?, state=Unknown] -> {desc.Reason}");
                        break;
                }
            }
        }

        protected TerminalServicesSession? MaybeConfigureSession(uint sid)
        {
            var info = QuerySessionInformation<WTSINFO>(sid, WTS_INFO_CLASS.WTSSessionInfo);

            // Filter: Port-Session
            if (info.SessionId == 0 || info.WinStationName == "Services")
                return null;
            // Filter: NonUser-Session
            if (string.IsNullOrEmpty(info.UserName))
                return null;

            var session = ConfigureSession(sid);

            return session;
        }

        protected virtual TerminalServicesSession ConfigureSession(uint sid)
        {
            return new TerminalServicesSession(sid)
            {
                Processes = ProcessManager.WithSessionId(sid)
            };
        }

        public virtual void Dispose()
        {
            foreach (var session in Sessions.Values)
                session.Dispose();
        }
    }
}
