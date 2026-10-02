using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Session.Manager;
using System.Collections;
using System.Diagnostics;

namespace DuoStreamIntegration.Tests;

internal sealed class FakeSession(uint id = 1, string? clientName = "Player", string userName = "player") : ISession
{
    public uint Id => id;
    public string UserName => userName;
    public string? ClientName => clientName;
    public bool IsConnected => clientName is not null;
    public bool IsConsoleConnected => false;
    public bool IsRemoteConnected => clientName is not null;
    public bool IsAdministrator => false;
    public bool IsUser => true;
    public bool? IsLocked => false;
    public TimeSpan? IdleTime => TimeSpan.Zero;
    public IProcess this[int pid] => throw new KeyNotFoundException();
    public Task Disconnect() => Task.CompletedTask;
    public Task Logoff() => Task.CompletedTask;
    public Task Lock() => Task.CompletedTask;
    public IProcess LaunchProcess(ProcessStartInfo info) => throw new NotSupportedException();
    public IEnumerator<IProcess> GetEnumerator() => Enumerable.Empty<IProcess>().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    private event EventHandler? _loggedOff;
    public TaskCompletionSource LogoffSubscribed { get; } = DuoTestSupport.Signal();
    public event EventHandler? LoggedOff
    {
        add { _loggedOff += value; LogoffSubscribed.TrySetResult(); }
        remove { _loggedOff -= value; }
    }
    public int LogoffSubscribers => _loggedOff?.GetInvocationList().Length ?? 0;
    public void RaiseLoggedOff() => _loggedOff?.Invoke(this, EventArgs.Empty);
    public event EventHandler Locked { add { } remove { } }
    public event EventHandler Unlocked { add { } remove { } }
    public event EventHandler Connected { add { } remove { } }
    public event EventHandler Disconnected { add { } remove { } }
    public event EventHandler<IProcess> ProcessStarted { add { } remove { } }
    public event EventHandler<IProcess> ProcessStopped { add { } remove { } }
}

internal sealed class FakeSessionManager(params ISession[] sessions) : ISessionManager
{
    private readonly Dictionary<uint, ISession> _sessions = sessions.ToDictionary(session => session.Id);
    public Action<uint>? SessionRequested { get; set; }
    public ISession this[uint id]
    {
        get
        {
            lock (_sessions)
            {
                try { return _sessions[id]; }
                finally { SessionRequested?.Invoke(id); }
            }
        }
    }
    public ISession? ConsoleSession { get; set; }
    public IEnumerable<ISession> FindSessionsByUserName(string user) => this.Where(session => session.UserName == user);
    public IEnumerator<ISession> GetEnumerator()
    {
        lock (_sessions) return _sessions.Values.ToList().GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Logon(ISession session)
    {
        lock (_sessions) _sessions[session.Id] = session;
        UserLogon?.Invoke(this, session);
    }
    public void Logoff(ISession session)
    {
        lock (_sessions) _sessions.Remove(session.Id);
        (session as FakeSession)?.RaiseLoggedOff();
        UserLogoff?.Invoke(this, session);
    }
    public int LogoffSubscribers => UserLogoff?.GetInvocationList().Length ?? 0;
    public event EventHandler<ISession>? UserLogon;
    public event EventHandler<ISession>? UserLogoff;
    public event EventHandler<ISession> RemoteConnect { add { } remove { } }
    public event EventHandler<ISession> ConsoleConnect { add { } remove { } }
    public event EventHandler<ISession> RemoteDisconnect { add { } remove { } }
    public event EventHandler<ISession> ConsoleDisconnect { add { } remove { } }
}
