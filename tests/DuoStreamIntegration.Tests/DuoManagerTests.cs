using MadWizard.Desomnia.Processes.Manager;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Reflection;
using Xunit;
using static MadWizard.Desomnia.Service.Duo.Manager.DuoInstance;

namespace DuoStreamIntegration.Tests;

public sealed class DuoManagerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Old_generation_command_cannot_use_replacement_API()
    {
        using var manager = CreateManager();
        var oldInstance = CreateInstance("old", "Player");
        var oldAPI = new ControlledDuoAPI();

        manager.EnqueueGeneration(oldAPI, oldInstance);
        Assert.True(await manager.StartGeneration(101));

        var currentInstance = CreateInstance("current", "Player");
        var currentAPI = new ControlledDuoAPI();
        manager.EnqueueGeneration(currentAPI, currentInstance);

        Assert.True(await manager.StartGeneration(202));
        await manager.Start(oldInstance);

        Assert.True(oldInstance.IsDisposed);
        Assert.Equal(0, oldAPI.StartCallCount);
        Assert.Equal(0, currentAPI.StartCallCount);
        Assert.Same(currentInstance, Assert.Single(manager));
    }

    [Fact]
    public async Task Replacement_drains_canceled_refresh_before_disposing_old_instance_resources()
    {
        using var manager = CreateManager();
        using var oldLifetime = new CancellationTokenSource();
        var oldInstance = CreateInstance("old", "Player");
        var refreshEntered = NewSignal();
        var refreshExited = NewSignal();
        var neverCompletes = NewSignal();
        var queryCount = 0;
        // DuoInstance.Dispose releases registry-backed and event-system resources. Seeing
        // it alive here proves retirement waits until the canceled refresh has unwound.
        var oldInstanceWasAliveWhenRefreshExited = false;

        var oldAPI = new ControlledDuoAPI
        {
            OnQuery = async (_name, cancellationToken) =>
            {
                if (Interlocked.Increment(ref queryCount) == 1)
                    return false;

                refreshEntered.TrySetResult();

                try
                {
                    await neverCompletes.Task.WaitAsync(cancellationToken);
                    return false;
                }
                catch (OperationCanceledException)
                {
                    oldInstanceWasAliveWhenRefreshExited = !oldInstance.IsDisposed;

                    throw;
                }
                finally
                {
                    refreshExited.TrySetResult();
                }
            }
        };

        manager.EnqueueGeneration(oldAPI, oldInstance);
        Assert.True(await manager.StartGeneration(101, oldLifetime.Token));

        var refresh = manager.RefreshGeneration();
        await refreshEntered.Task.WaitAsync(TestTimeout);

        var currentInstance = CreateInstance("current", "Player");
        manager.EnqueueGeneration(new ControlledDuoAPI(), currentInstance);
        var replacement = manager.StartGeneration(202);

        Assert.False(replacement.IsCompleted);
        Assert.False(oldInstance.IsDisposed);

        oldLifetime.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => refresh.WaitAsync(TestTimeout));
        await refreshExited.Task.WaitAsync(TestTimeout);
        Assert.True(await replacement.WaitAsync(TestTimeout));

        Assert.True(oldInstanceWasAliveWhenRefreshExited);
        Assert.True(oldInstance.IsDisposed);
        Assert.Same(currentInstance, Assert.Single(manager));
    }

    [Fact]
    public async Task Start_subscribes_before_API_can_publish_state_change()
    {
        using var manager = CreateManager();
        var instance = CreateInstance("generation", "Player");
        var baselineHandlers = HandlerCount(instance, nameof(DuoInstance.RunningStateChanged));
        var handlersAtAPICall = -1;

        var api = new ControlledDuoAPI
        {
            OnStart = (_, _) =>
            {
                handlersAtAPICall = HandlerCount(instance, nameof(DuoInstance.RunningStateChanged));
                instance.SetRunningState(true);
                return Task.CompletedTask;
            }
        };

        manager.EnqueueGeneration(api, instance);
        Assert.True(await manager.StartGeneration(101));

        await manager.Start(instance, timeout: 500);

        Assert.Equal(baselineHandlers + 1, handlersAtAPICall);
        Assert.Equal(baselineHandlers, HandlerCount(instance, nameof(DuoInstance.RunningStateChanged)));
        Assert.True(instance.IsRunning);
        Assert.Equal(1, api.StartCallCount);
    }

    [Fact]
    public async Task Start_without_state_event_throws_SystemTimeoutException()
    {
        using var manager = CreateManager();
        var instance = CreateInstance("generation", "Player");
        var api = new ControlledDuoAPI();
        manager.EnqueueGeneration(api, instance);
        Assert.True(await manager.StartGeneration(101));

        using var testGuard = new CancellationTokenSource(TestTimeout);
        var exception = await Assert.ThrowsAsync<System.TimeoutException>(
            () => manager.Start(instance, timeout: 50, testGuard.Token));

        Assert.Contains("Timed out while starting", exception.Message);
        Assert.Equal(1, api.StartCallCount);
    }

    [Fact]
    public async Task Throwing_lifecycle_subscriber_does_not_hide_exact_old_snapshot_from_later_subscribers()
    {
        using var manager = CreateManager();
        var firstOldInstance = CreateInstance("old", "Alpha");
        var secondOldInstance = CreateInstance("old", "Beta");
        var currentInstance = CreateInstance("current", "Gamma");
        DuoLifecycleEventArgs? firstArgs = null;
        DuoLifecycleEventArgs? secondArgs = null;
        var firstCalls = 0;
        var secondCalls = 0;
        var firstSawOldInstancesAlive = false;
        var secondSawOldInstancesAlive = false;

        manager.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref firstCalls);
            firstArgs = args;
            firstSawOldInstancesAlive = !firstOldInstance.IsDisposed && !secondOldInstance.IsDisposed;
            throw new InvalidOperationException("test subscriber failure");
        };
        manager.Stopped += (_, args) =>
        {
            Interlocked.Increment(ref secondCalls);
            secondArgs = args;
            secondSawOldInstancesAlive = !firstOldInstance.IsDisposed && !secondOldInstance.IsDisposed;
        };

        manager.EnqueueGeneration(new ControlledDuoAPI(), firstOldInstance, secondOldInstance);
        Assert.True(await manager.StartGeneration(101));

        manager.EnqueueGeneration(new ControlledDuoAPI(), currentInstance);
        Assert.True(await manager.StartGeneration(202));

        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);
        Assert.True(firstSawOldInstancesAlive);
        Assert.True(secondSawOldInstancesAlive);
        Assert.NotNull(firstArgs);
        Assert.Same(firstArgs, secondArgs);
        Assert.Equal((uint)101, secondArgs!.ProcessId);
        Assert.Equal([firstOldInstance, secondOldInstance], secondArgs.Instances);
        Assert.True(firstOldInstance.IsDisposed);
        Assert.True(secondOldInstance.IsDisposed);
        Assert.Same(currentInstance, Assert.Single(manager));
    }

    [Fact]
    public async Task Process_exit_invalidates_generation_and_retirement_detaches_subscription_once()
    {
        using var manager = CreateManager();
        var process = new FakeProcess(101);
        var signalCount = 0;
        var detachedWhenStopped = false;
        var subscription = new DuoProcessSubscription(
            process,
            CancellationToken.None,
            () => Interlocked.Increment(ref signalCount));
        var lateCallback = process.SnapshotStoppedHandlers();

        manager.Stopped += (_, _) => detachedWhenStopped = process.StoppedHandlerCount == 0;

        manager.EnqueueGeneration(new ControlledDuoAPI(), CreateInstance("generation", "Player"));
        Assert.True(await manager.StartGeneration(101, subscription.Token, subscription));
        Assert.Equal(1, process.StoppedHandlerCount);

        process.RaiseStopped();

        Assert.True(subscription.Token.IsCancellationRequested);
        Assert.Equal(1, signalCount);
        Assert.True(await manager.StopGeneration(101));

        Assert.Equal(0, process.StoppedHandlerCount);
        Assert.Equal(1, process.StoppedHandlerRemoveCount);
        Assert.True(detachedWhenStopped);

        process.RaiseStopped();
        subscription.Dispose();
        lateCallback?.Invoke(process, EventArgs.Empty);

        Assert.Equal(2, signalCount);
        Assert.Equal(1, process.StoppedHandlerRemoveCount);
    }

    [Fact]
    public async Task Manager_dispose_detaches_active_process_subscription_synchronously()
    {
        var manager = CreateManager();
        var process = new FakeProcess(101);
        var subscription = new DuoProcessSubscription(process, CancellationToken.None, () => { });

        manager.EnqueueGeneration(new ControlledDuoAPI(), CreateInstance("generation", "Player"));
        Assert.True(await manager.StartGeneration(101, subscription.Token, subscription));

        manager.Dispose();
        manager.Dispose();

        Assert.True(subscription.Token.IsCancellationRequested);
        Assert.Equal(0, process.StoppedHandlerCount);
        Assert.Equal(1, process.StoppedHandlerRemoveCount);
    }

    [Fact]
    public async Task Failed_generation_adoption_releases_process_subscription()
    {
        using var manager = CreateManager();
        var process = new FakeProcess(101);
        var subscription = new DuoProcessSubscription(process, CancellationToken.None, () => { });
        var instance = CreateInstance("generation", "Player");
        var api = new ControlledDuoAPI
        {
            OnQuery = (_, _) => throw new InvalidOperationException("Duo API unavailable")
        };

        manager.EnqueueGeneration(api, instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.StartGeneration(101, subscription.Token, subscription));

        Assert.True(subscription.Token.IsCancellationRequested);
        Assert.True(instance.IsDisposed);
        Assert.Equal(0, process.StoppedHandlerCount);
        Assert.Equal(1, process.StoppedHandlerRemoveCount);
        Assert.Empty(manager);
    }

    internal static TestDuoManager CreateManager()
    {
        return new TestDuoManager
        {
            Logger = NullLogger<DuoManager>.Instance,
            SessionManager = new EmptySessionManager(),
            CreateInstance = (_, _) => throw new NotSupportedException("Tests override LoadInstances.")
        };
    }

    internal static DuoInstance CreateInstance(string generation, string name)
    {
        return new DuoInstance(
            new DuoInstanceInfo { Name = name }, 
            CreateSettings(name, userName: $"{generation}-{name}-user"));
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int HandlerCount(DuoInstance instance, string eventName)
    {
        var field = typeof(DuoInstance).GetField(
            eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        var handlers = field?.GetValue(instance) as Delegate;
        return handlers?.GetInvocationList().Length ?? 0;
    }

    internal static DuoInstance.InstanceSettings CreateSettings(
        string name,
        ushort port = 47989,
        string userName = "test",
        bool isSandboxed = true)
    {
        return new InstanceSettings
        {
            Name = name,
            Port = port,
            UserName = userName,
            IsSandboxed = isSandboxed
        };
    }
}

internal sealed class TestDuoManager : DuoManager
{
    private readonly ConcurrentQueue<DuoApiConnection> _apis = new();
    private readonly ConcurrentQueue<List<DuoInstance>> _instances = new();

    public TestDuoManager()
        : base(new DuoSessionMonitorConfig { ServiceName = "DuoTestService" })
    {
    }

    public void EnqueueGeneration(IDuoWebManager api, params DuoInstance[] instances)
    {
        _apis.Enqueue(new DuoApiConnection(api, api as IDisposable));
        _instances.Enqueue([.. instances]);
    }

    public Task<bool> StartGeneration(
        uint processId,
        CancellationToken lifetimeToken = default,
        IDisposable? lifetimeRegistration = null)
        => TriggerStarted(processId, lifetimeToken, lifetimeRegistration);

    public Task RefreshGeneration(CancellationToken cancellationToken = default)
        => TriggerRefresh(cancellationToken);

    public Task<bool> StopGeneration(uint? expectedProcessId = null)
        => TriggerStopped(expectedProcessId);

    public Task ReconcileService(uint? processId)
        => Reconcile(processId, CancellationToken.None);

    internal override DuoApiConnection CreateAPI()
        => _apis.TryDequeue(out var api)
            ? api
            : throw new InvalidOperationException("No API was queued for this generation.");

    internal override (string Path, Version Version) GetServiceInfo()
        => ("duo-test.exe", new Version(1, 0));

    protected override List<DuoInstance> LoadInstances()
        => _instances.TryDequeue(out var instances)
            ? instances
            : throw new InvalidOperationException("No instances were queued for this generation.");

    protected override Task RunAsync(CancellationToken stoppingToken)
        => Task.CompletedTask;
}

internal sealed class ControlledDuoAPI : IDuoWebManager, IDisposable
{
    private int _queryCallCount;
    private int _startCallCount;
    private int _stopCallCount;
    private int _disposeCallCount;

    public Func<string, CancellationToken, Task<bool>> OnQuery { get; init; }
        = (_, _) => Task.FromResult(false);

    public Func<string, CancellationToken, Task> OnStart { get; init; }
        = (_, _) => Task.CompletedTask;

    public Func<string, CancellationToken, Task> OnStop { get; init; }
        = (_, _) => Task.CompletedTask;

    public int QueryCallCount => Volatile.Read(ref _queryCallCount);
    public int StartCallCount => Volatile.Read(ref _startCallCount);
    public int StopCallCount => Volatile.Read(ref _stopCallCount);
    public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

    public async Task<bool> QueryInstance(string name, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _queryCallCount);
        return await OnQuery(name, cancellationToken);
    }

    public async Task StartInstance(string name, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _startCallCount);
        await OnStart(name, cancellationToken);
    }

    public async Task StopInstance(string name, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _stopCallCount);
        await OnStop(name, cancellationToken);
    }

    public void Dispose() => Interlocked.Increment(ref _disposeCallCount);
}

internal sealed class EmptySessionManager : ISessionManager
{
    public ISession this[uint sid] => throw new KeyNotFoundException();

    public ISession? ConsoleSession { get; set; }

    public IEnumerable<ISession> FindSessionsByUserName(string user) => [];

    public IEnumerator<ISession> GetEnumerator()
        => Enumerable.Empty<ISession>().GetEnumerator();

    public event EventHandler<ISession> UserLogon { add { } remove { } }
    public event EventHandler<ISession> RemoteConnect { add { } remove { } }
    public event EventHandler<ISession> ConsoleConnect { add { } remove { } }
    public event EventHandler<ISession> RemoteDisconnect { add { } remove { } }
    public event EventHandler<ISession> ConsoleDisconnect { add { } remove { } }
    public event EventHandler<ISession> UserLogoff { add { } remove { } }
}

internal sealed class FakeProcess(int id) : IProcess
{
    private EventHandler? _stopped;
    private int _stoppedHandlerCount;
    private int _stoppedHandlerRemoveCount;

    public int Id { get; } = id;
    public int SessionId => 0;
    public string Name => "DuoTestProcess";
    public string? ImagePath => null;
    public TimeSpan? ProcessorTime => TimeSpan.Zero;
    public IProcess? Parent => null;
    public bool HasStopped { get; private set; }
    public System.Diagnostics.Process Native => throw new NotSupportedException();
    public int StoppedHandlerCount => Volatile.Read(ref _stoppedHandlerCount);
    public int StoppedHandlerRemoveCount => Volatile.Read(ref _stoppedHandlerRemoveCount);

    public event EventHandler Stopped
    {
        add
        {
            _stopped += value;
            Interlocked.Increment(ref _stoppedHandlerCount);
        }
        remove
        {
            _stopped -= value;
            Interlocked.Decrement(ref _stoppedHandlerCount);
            Interlocked.Increment(ref _stoppedHandlerRemoveCount);
        }
    }

    public Task Stop(TimeSpan timeout = default)
    {
        RaiseStopped();
        return Task.CompletedTask;
    }

    public void RaiseStopped()
    {
        HasStopped = true;
        _stopped?.Invoke(this, EventArgs.Empty);
    }

    public EventHandler? SnapshotStoppedHandlers() => _stopped;

    public void Dispose() { }
}
