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

    public static DuoServiceContext Context(IDuoManager manager, IDuoWatcher watcher, params DuoInstance[] instances)
    {
        return new DuoServiceContext
        {
            Logger = NullLogger<DuoServiceContext>.Instance,
            Manager = manager, Watcher = watcher, Instances = instances
        };
    }

    public static async Task StartContext(DuoServiceContext context)
    {
        await context.InitializeAsync(TestTimeout);
        context.StartWatching();
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
        Manager = manager
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
    private readonly ConcurrentDictionary<DuoInstance, bool> _states = new();
    public void SetState(DuoInstance instance, bool running) => _states[instance] = running;
    public Func<DuoInstance, CancellationToken, Task<bool>>? OnQuery { get; set; }
    public Func<DuoInstance, bool, CancellationToken, Task> OnChange { get; set; } = (_, _, _) => Task.CompletedTask;

    public Task<bool> QueryState(DuoInstance instance, CancellationToken token = default)
    {
        Interlocked.Increment(ref _queries);
        return OnQuery?.Invoke(instance, token) ?? Task.FromResult(_states.GetValueOrDefault(instance));
    }

    public Task ChangeState(DuoInstance instance, bool running, CancellationToken token = default)
    {
        if (running) Interlocked.Increment(ref _starts); else Interlocked.Increment(ref _stops);
        return OnChange(instance, running, token);
    }
}

// Session attachment and backend state can be varied independently.
// PublishAsync represents a completed start/stop transition.
internal sealed class ControlledWatcher : IDuoWatcher
{
    public required IDuoManager Manager { get; init; }
    private readonly ObservedChannel<WatchSignal> _signals = new();
    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    public async IAsyncEnumerable<WatchSignal> WatchAsync(IEnumerable<DuoInstance> instances,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        Started = true;
        try
        {
            await foreach (var signal in _signals.Reader.ReadAllAsync(token))
                yield return signal;
        }
        finally
        {
            Stopped = true;
            _signals.Writer.TryComplete();
        }
    }

    public static void SetSession(DuoInstance instance, ISession? session)
    {
        lock (instance)
        {
            foreach (var watch in instance.OfType<SessionWatch>().ToArray())
            {
                instance.StopTracking(watch);
                watch.Dispose();
            }
            if (session is not null) instance.StartTracking(DuoTestSupport.SessionWatch(session));
        }
    }

    public Task PublishAsync(DuoInstance instance, bool running)
    {
        if (Manager is ControlledManager manager) manager.SetState(instance, running);
        SetSession(instance, running ? DuoTestSupport.SessionFor(instance) : null);
        return SignalAsync(instance, running);
    }

    public Task SignalAsync(DuoInstance? instance = null, bool? running = null) =>
        _signals.Write(new WatchSignal(instance, running)).WaitAsync(DuoTestSupport.TestTimeout);
}

// Observe the real watcher stream without interpreting or changing its signals.
internal sealed class WatchRun : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Threading.Channels.Channel<WatchSignal> _signals =
        System.Threading.Channels.Channel.CreateUnbounded<WatchSignal>();
    private readonly Task _watching;
    private bool _disposed;
    public WatchRun(IDuoWatcher watcher, params DuoInstance[] instances) => _watching = Run(watcher, instances);
    private async Task Run(IDuoWatcher watcher, DuoInstance[] instances)
    {
        try
        {
            await foreach (var signal in watcher.WatchAsync(instances, _lifetime.Token))
                _signals.Writer.TryWrite(signal);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _signals.Writer.TryComplete(); }
    }
    public Task<WatchSignal> Next() => _signals.Reader.ReadAsync().AsTask().WaitAsync(DuoTestSupport.TestTimeout);
    public bool TryRead(out WatchSignal signal) => _signals.Reader.TryRead(out signal);
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        await _watching.WaitAsync(DuoTestSupport.TestTimeout);
        _lifetime.Dispose();
    }
}

internal sealed class RecordingLogger<T> : RecordingLogger, ILogger<T> { }

internal class RecordingLogger : ILogger
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
