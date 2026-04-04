using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Service;
using Microsoft.Extensions.Logging;
using System.ServiceProcess;
using static MadWizard.Desomnia.Session.Manager.TerminalServicesSession;

namespace MadWizard.Desomnia.Session.Manager
{
    public partial class TerminalServicesManager : ISessionManager, IDisposable
    {
        public required ILogger<TerminalServicesManager> Logger { protected get; init; }

        public required Func<uint, Owned<TerminalServicesSession>> ConfigureSession { private get; init; }

        public TerminalServicesManager(WindowsService service)
        {
            service.SessionChanged += Service_SessionChanged;
        }

        protected Dictionary<uint, Owned<TerminalServicesSession>> Sessions
        {
            get
            {
                if (field == null )
                {
                    field = EnumerateSessionIDs()
                        .Select(MaybeConfigureSession)
                        .Where(s => s != null).Select(s => s!)
                        .ToDictionary(s => s.Value.Id);

                    Logger.LogDebug($"Enumerating existing user sessions:");

                    foreach (var owned in field.Values)
                    {
                        Logger.LogDebug($"{owned.Value}");
                    }

                    Logger.LogDebug($"Startup of {GetType().Name} complete.");
                }

                return field;
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
            foreach (var session in Sessions.Values)
                session.Dispose();
        }
    }
}
