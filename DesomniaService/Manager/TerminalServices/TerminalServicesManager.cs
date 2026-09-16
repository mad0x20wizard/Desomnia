using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Service;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ServiceProcess;
using static MadWizard.Desomnia.Session.Manager.TerminalServicesSession;

namespace MadWizard.Desomnia.Session.Manager
{
    public partial class TerminalServicesManager : ISessionManager, IDisposable
    {
        public required ILogger<TerminalServicesManager> Logger { protected get; init; }

        public required Func<uint, Owned<TerminalServicesSession>> ConfigureSession { private get; init; }

        private readonly WindowsService _service;

        private Dictionary<uint, Owned<TerminalServicesSession>>? _sessions;

        private bool _disposed;

        public TerminalServicesManager(WindowsService service)
        {
            _service = service;

            service.SessionChanged += Service_SessionChanged;
        }

        protected Dictionary<uint, Owned<TerminalServicesSession>> Sessions
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var ids = EnumerateSessionIDs();

                if (_sessions == null)
                {
                    _sessions = ids.Select(MaybeConfigureSession).OfType<Owned<TerminalServicesSession>>().ToDictionary(s => s.Value.Id);

                    Logger.LogDebug($"Enumerating existing user sessions:");

                    foreach (var owned in _sessions.Values)
                    {
                        Logger.LogDebug($"{owned.Value}");
                    }

                    Logger.LogDebug($"Startup of {GetType().Name} complete.");
                }
                else
                {
                    /// Sometimes the Terminal Server removes the session
                    /// from it's table even before it sent the SessionLogoff event.
                    /// This produces Win32Exceptions throughout the program.
                    /// To prevent this from happening, we try to remove 
                    /// these sessions eagerly here.
                    foreach (var missing in _sessions.Keys.Except(ids).ToArray())
                    {
                        if (_sessions.Remove(missing, out var scope))
                        {
                            Logger.LogWarning("WTSSession[id={ID}, name=?, state=Unknown] -> gone", missing);

                            scope.Dispose(); // All that remains for us is to remove them
                        }
                    }
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

        public ISession this[uint sid] => Sessions[sid].Value;

        public ISession? ConsoleSession
        {
            get
            {
                var consoleSID = WTSGetActiveConsoleSessionId();

                return Sessions.TryGetValue(consoleSID, out var session) ? session.Value : null;
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

        public IEnumerable<ISession> FindSessionsByUserName(string user)
        {
            return Sessions.Values.Where(s => s.Value.UserName.Equals(user, StringComparison.InvariantCultureIgnoreCase)).Select(o => o.Value);
        }

        public IEnumerator<ISession> GetEnumerator()
        {
            return Sessions.Values.Select(o => o.Value).GetEnumerator();
        }

        private async void Service_SessionChanged(object? sender, SessionChangeDescription desc)
        {
            uint sid = (uint)desc.SessionId;

            Sessions.TryGetValue(sid, out var owned);

            if (owned != null && owned.Value is var session)
            {
                try
                {
                    Logger.LogDebug($"{session} -> {desc.Reason}");

                    switch (desc.Reason)
                    {
                        case SessionChangeReason.SessionLogoff:
                            if (Sessions.Remove(sid, out var scope))
                            {
                                UserLogoff?.Invoke(this, session);

                                scope.Dispose();
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
                catch (Win32Exception ex) when (ex.NativeErrorCode == 2) // Das System kann die angegebene Datei nicht finden.
                {
                    Logger.LogError($"WTSSession[id={desc.SessionId}, name=?, state=Unknown] -> {desc.Reason}: {ex.Message}");

                    if (Sessions.Remove(sid, out var scope))
                    {
                        scope.Dispose();
                    }
                }
            }
            else
            {
                switch (desc.Reason)
                {
                    case SessionChangeReason.SessionLogon when MaybeConfigureSession(sid) is Owned<TerminalServicesSession> configured:
                        Logger.LogDebug($"{configured.Value} -> {desc.Reason}");

                        Sessions[sid] = configured;

                        UserLogon?.Invoke(this, configured.Value);

                        break;

                    default:
                        Logger.LogDebug($"WTSSession[id={desc.SessionId}, name=?, state=Unknown] -> {desc.Reason}");
                        break;
                }
            }
        }

        protected Owned<TerminalServicesSession>? MaybeConfigureSession(uint sid)
        {
            var info = QuerySessionInformation<WTSINFO>(sid, WTS_INFO_CLASS.WTSSessionInfo);

            // Filter: Port-Session
            if (info.SessionId == 0 || info.WinStationName == "Services")
                return null;
            // Filter: NonUser-Session
            if (string.IsNullOrEmpty(info.UserName))
                return null;

            return ConfigureSession(sid);
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                _service.SessionChanged -= Service_SessionChanged;

                if (_sessions is not null)
                {
                    foreach (var session in _sessions.Values)
                    {
                        session.Dispose();
                    }

                    _sessions = null;
                }
            }
        }
    }
}
