using Autofac.Features.OwnedInstances;
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

    public static DuoServiceContext Context(IDuoManager manager, IDuoInstanceWatcher watcher, params DuoInstance[] instances) => new()
    {
        Settings = new DuoSettings { Port = 38299, Instances = [.. instances.Select(i => i.Settings)] },
        Manager = manager, Watcher = watcher, Instances = instances
    };

    public static ControlledWatcher Watcher(IDuoManager manager) => new() { Manager = manager, Logger = NullLogger.Instance };

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

internal sealed class ControlledWatcher : BaseWatcher
{
    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    public override async Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken token)
    {
        Started = true;
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Stopped = true; }
    }

    public void Publish(DuoInstance instance, bool running) => NotifyInstanceStatus(instance, running);
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
