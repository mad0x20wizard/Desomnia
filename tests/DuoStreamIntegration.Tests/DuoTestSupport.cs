using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Manager;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Processes;
using MadWizard.Desomnia.Service.Controller;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.ServiceProcess;

namespace DuoStreamIntegration.Tests;

internal static class DuoTestSupport
{
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static InstanceSettings Settings(string name = "Player", string userName = "player", bool sandboxed = false) => new()
    {
        Name = name, DisplayName = name, Port = 47989, UserName = userName, IsSandboxed = sandboxed
    };

    public static DuoInstance Instance(string name = "Player") =>
        new(name, Settings(name), new DuoInstanceWatchInfo { Name = name });

    public static DuoServiceContext Context(IDuoManager manager, IDuoSessionWatcher watcher, params DuoInstance[] instances)
    {
        var monitor = new SessionMonitor(new SessionMonitorConfig(), new FakeSessionManager())
        {
            Logger = NullLogger<SessionMonitor>.Instance, Scope = null!
        };
        monitor.StartupFinished.Set();

        // Supply SessionMonitor's side of the association before the real context handles it.
        // Each new instance session gets a fresh watch, just as a Windows logon would.
        var watches = new Dictionary<DuoInstance, SessionWatch>();
        watcher.SessionChanged += (_, args) =>
        {
            lock (watches)
            {
                if (watches.Remove(args.Instance, out var previous))
                {
                    monitor.StopTracking(previous);
                    previous.Dispose();
                }
                if (args.Session is not null)
                {
                    var watch = SessionWatch(args.Session);
                    watches[args.Instance] = watch;
                    monitor.StartTracking(watch);
                }
            }
        };
        return new DuoServiceContext
        {
            Settings = new DuoSettings { Port = 38299, Instances = [.. instances.Select(i => i.Settings)] },
            Manager = manager, Watcher = watcher, Instances = instances,
            SessionMonitor = monitor, SessionMonitorConfig = new SessionMonitorConfig()
        };
    }

    public static SessionWatch SessionWatch(ISession session) => new(session)
    {
        Session = session,
        CreateAnyProcessWatch = metrics => new AnySessionProcessWatch
        {
            Manager = session, MetricsWatch = new ProcessMetricsWatch(metrics)
        },
        CreateProcessWatch = _ => throw new InvalidOperationException("No process watches configured.")
    };
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuoInstance, FakeSession> Sessions = new();
    public static FakeSession SessionFor(DuoInstance instance) => Sessions.GetValue(instance,
        target => new FakeSession(clientName: target.Name, userName: target.Settings.UserName));

    public static ControlledWatcher Watcher(IDuoManager manager) => new()
    {
        Manager = manager, Logger = NullLogger.Instance, SessionManager = new FakeSessionManager()
    };

    public static DuoSessionMonitor Monitor(FakeDuoService service, Func<DuoSettings, Owned<DuoServiceContext>> create) => new(service.Service)
    {
        Logger = NullLogger<DuoSessionMonitor>.Instance,
        CreateContext = create
    };
}

internal sealed class ControlledManager : IDuoManager
{
    private int _queries, _starts, _stops;
    public int Queries => Volatile.Read(ref _queries);
    public int Starts => Volatile.Read(ref _starts);
    public int Stops => Volatile.Read(ref _stops);
    public Func<DuoInstance, CancellationToken, Task<bool>> OnQuery { get; set; } = (_, _) => Task.FromResult(false);
    public Func<DuoInstance, bool, CancellationToken, Task> OnChange { get; set; } = (_, _, _) => Task.CompletedTask;

    public Task<bool> QueryRunningState(DuoInstance instance, CancellationToken token = default)
    {
        Interlocked.Increment(ref _queries);
        return OnQuery(instance, token);
    }

    public Task ChangeState(DuoInstance instance, bool running, CancellationToken token = default)
    {
        if (running) Interlocked.Increment(ref _starts); else Interlocked.Increment(ref _stops);
        return OnChange(instance, running, token);
    }
}

internal sealed class ControlledWatcher : StatusWatcher
{
    protected override ISession? FindSession(DuoInstance instance) =>
        SessionManager.FirstOrDefault(session => session.ClientName == instance.Name) ?? DuoTestSupport.SessionFor(instance);

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    protected override async Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token)
    {
        Started = true;
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Stopped = true; }
    }

    public void Publish(DuoInstance instance, bool running) => NotifyInstanceStatus(instance, running);
    private readonly Dictionary<DuoInstance, ISession> _published = [];
    public void Publish(DuoInstance instance, ISession? session)
    {
        if (_published.GetValueOrDefault(instance) == session) return;
        if (session is null) _published.Remove(instance); else _published[instance] = session;
        PublishSessionChange(instance, session);
    }
}


internal sealed class RecordingLogger : ILogger
{
    public ConcurrentQueue<Exception> Errors { get; } = new();
    public TaskCompletionSource ErrorReported { get; } = DuoTestSupport.Signal();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (exception is not null)
        {
            Errors.Enqueue(exception);
            ErrorReported.TrySetResult();
        }
    }
}
