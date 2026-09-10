using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DuoStreamIntegration.Tests.DuoManagerTests;

namespace DuoStreamIntegration.Tests;

public sealed class DuoConcurrencyTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Same_instance_commands_are_serialized(bool secondRunning)
    {
        using var manager = CreateManager();
        var instance = CreateInstance("generation", "Player");
        var entered = NewSignal();
        var release = NewSignal();
        var api = new ControlledDuoAPI
        {
            OnStart = async (_, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                instance.SetRunningState(true);
            },
            OnStop = (_, _) =>
            {
                instance.SetRunningState(false);
                return Task.CompletedTask;
            }
        };
        manager.EnqueueGeneration(api, instance);
        await manager.StartGeneration(101);

        var first = manager.Start(instance);
        await entered.Task.WaitAsync(TestTimeout);
        var second = secondRunning ? manager.Start(instance) : manager.Stop(instance);

        try
        {
            Assert.False(second.IsCompleted);
            Assert.Equal(1, api.StartCallCount);
            Assert.Equal(0, api.StopCallCount);
        }
        finally
        {
            release.TrySetResult();
        }

        await Task.WhenAll(first, second).WaitAsync(TestTimeout);
        Assert.Equal(secondRunning, instance.IsRunning);
        Assert.Equal(1, api.StartCallCount);
        Assert.Equal(secondRunning ? 0 : 1, api.StopCallCount);
        Assert.False(instance.IsBusy);
    }

    [Fact]
    public async Task Different_instances_can_start_concurrently()
    {
        using var manager = CreateManager();
        var alpha = CreateInstance("generation", "Alpha");
        var beta = CreateInstance("generation", "Beta");
        var bothEntered = NewSignal();
        var count = 0;
        var api = new ControlledDuoAPI
        {
            OnStart = async (name, token) =>
            {
                if (Interlocked.Increment(ref count) == 2)
                    bothEntered.TrySetResult();

                await bothEntered.Task.WaitAsync(token);
                (name == alpha.Name ? alpha : beta).SetRunningState(true);
            }
        };
        manager.EnqueueGeneration(api, alpha, beta);
        await manager.StartGeneration(101);

        await Task.WhenAll(manager.Start(alpha), manager.Start(beta)).WaitAsync(TestTimeout);

        Assert.Equal(2, api.StartCallCount);
        Assert.True(alpha.IsRunning);
        Assert.True(beta.IsRunning);
    }

    [Fact]
    public async Task Retirement_cancels_queued_commands_and_drains_in_flight_API()
    {
        using var manager = CreateManager();
        var instance = CreateInstance("generation", "Player");
        var entered = NewSignal();
        var canceled = NewSignal();
        var release = NewSignal();
        var api = new ControlledDuoAPI
        {
            OnStart = async (_, token) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    canceled.TrySetResult();
                    await release.Task;
                }
            }
        };
        manager.EnqueueGeneration(api, instance);
        await manager.StartGeneration(101);

        var first = manager.Start(instance);
        await entered.Task.WaitAsync(TestTimeout);
        var queued = manager.Start(instance);
        var retirement = manager.StopGeneration();

        try
        {
            await canceled.Task.WaitAsync(TestTimeout);
            await queued.WaitAsync(TestTimeout);
            Assert.False(retirement.IsCompleted);
            Assert.False(instance.IsDisposed);
            Assert.Equal(0, api.DisposeCallCount);
        }
        finally
        {
            release.TrySetResult();
        }

        await Task.WhenAll(first, retirement).WaitAsync(TestTimeout);
        Assert.True(instance.IsDisposed);
        Assert.Equal(1, api.StartCallCount);
        Assert.Equal(1, api.DisposeCallCount);
        Assert.False(instance.IsBusy);
    }

    [Fact]
    public async Task State_actions_can_await_commands_without_blocking_refresh()
    {
        using var manager = CreateManager();
        var instance = CreateInstance("generation", "Player");
        var running = false;
        var stopRequested = NewSignal();
        var actionCompleted = NewSignal();
        var api = new ControlledDuoAPI
        {
            OnQuery = (_, _) => Task.FromResult(running),
            OnStop = (_, _) =>
            {
                running = false;
                stopRequested.TrySetResult();
                return Task.CompletedTask;
            }
        };
        manager.EnqueueGeneration(api, instance);
        await manager.StartGeneration(101);
        instance.Started += async _ =>
        {
            await manager.Stop(instance);
            actionCompleted.TrySetResult();
        };

        running = true;
        await manager.RefreshGeneration().WaitAsync(TestTimeout);
        await stopRequested.Task.WaitAsync(TestTimeout);
        Assert.False(actionCompleted.Task.IsCompleted);

        await manager.RefreshGeneration().WaitAsync(TestTimeout);
        await actionCompleted.Task.WaitAsync(TestTimeout);
        Assert.False(instance.IsRunning);
    }

    [Fact]
    public async Task Retirement_drains_state_actions_before_disposing_resources()
    {
        using var manager = CreateManager();
        var instance = CreateInstance("generation", "Player");
        var running = false;
        var entered = NewSignal();
        var release = NewSignal();
        var api = new ControlledDuoAPI { OnQuery = (_, _) => Task.FromResult(running) };
        manager.EnqueueGeneration(api, instance);
        await manager.StartGeneration(101);
        instance.Started += async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        running = true;
        await manager.RefreshGeneration().WaitAsync(TestTimeout);
        await entered.Task.WaitAsync(TestTimeout);
        var retirement = manager.StopGeneration();

        try
        {
            Assert.False(retirement.IsCompleted);
            Assert.False(instance.IsDisposed);
            Assert.Equal(0, api.DisposeCallCount);
        }
        finally
        {
            release.TrySetResult();
        }

        await retirement.WaitAsync(TestTimeout);
        Assert.True(instance.IsDisposed);
        Assert.Equal(1, api.DisposeCallCount);
    }

    [Fact]
    public async Task Reconciliation_refreshes_replaces_and_stops_from_observed_process_id()
    {
        using var manager = CreateManager();
        var oldInstance = CreateInstance("old", "Player");
        var oldAPI = new ControlledDuoAPI();
        var currentInstance = CreateInstance("current", "Player");
        var currentAPI = new ControlledDuoAPI();
        var transitions = new List<string>();
        manager.Started += (_, args) => transitions.Add($"start:{args.ProcessId}");
        manager.Stopped += (_, args) => transitions.Add($"stop:{args.ProcessId}");
        manager.EnqueueGeneration(oldAPI, oldInstance);
        manager.EnqueueGeneration(currentAPI, currentInstance);

        await manager.ReconcileService(101);
        await manager.ReconcileService(101);
        Assert.Equal(2, oldAPI.QueryCallCount);
        Assert.Equal(["start:101"], transitions);

        await manager.ReconcileService(202);
        Assert.True(oldInstance.IsDisposed);
        Assert.Equal(1, oldAPI.DisposeCallCount);
        Assert.Same(currentInstance, Assert.Single(manager));

        await manager.ReconcileService(null);
        Assert.Equal(["start:101", "stop:101", "start:202", "stop:202"], transitions);
        Assert.True(currentInstance.IsDisposed);
        Assert.Equal(1, currentAPI.DisposeCallCount);
        Assert.Empty(manager);
    }

    [Fact]
    public async Task Reconciliation_replaces_an_invalidated_generation_with_the_same_process_id()
    {
        using var manager = CreateManager();
        using var lifetime = new CancellationTokenSource();
        var oldInstance = CreateInstance("old", "Player");
        var currentInstance = CreateInstance("current", "Player");
        manager.EnqueueGeneration(new ControlledDuoAPI(), oldInstance);
        await manager.StartGeneration(101, lifetime.Token);

        lifetime.Cancel();
        manager.EnqueueGeneration(new ControlledDuoAPI(), currentInstance);
        await manager.ReconcileService(101);

        Assert.True(oldInstance.IsDisposed);
        Assert.Same(currentInstance, Assert.Single(manager));
    }

    [Fact]
    public async Task Caller_cancellation_releases_command_waiter_and_semaphore()
    {
        using var manager = CreateManager();
        using var cancellation = new CancellationTokenSource();
        var instance = CreateInstance("generation", "Player");
        var calls = 0;
        var api = new ControlledDuoAPI
        {
            OnStart = (_, _) =>
            {
                if (++calls == 2)
                    instance.SetRunningState(true);
                return Task.CompletedTask;
            }
        };
        manager.EnqueueGeneration(api, instance);
        await manager.StartGeneration(101);

        var first = manager.Start(instance, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestTimeout));
        Assert.False(instance.IsBusy);

        await manager.Start(instance).WaitAsync(TestTimeout);
        Assert.True(instance.IsRunning);
        Assert.Equal(2, api.StartCallCount);
    }

    [Fact]
    public async Task Disposal_waits_for_cancellation_already_running_on_another_thread()
    {
        var instance = CreateInstance("generation", "Player");
        var api = new ControlledDuoAPI();
        await using var generation = new DuoGeneration(
            101, new DuoApiConnection(api, api), [instance], CancellationToken.None, null, NullLogger.Instance);
        var entered = NewSignal();
        var release = NewSignal();
        using var callback = generation.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });

        var cancel = Task.Run(generation.CancelAsync);
        await entered.Task.WaitAsync(TestTimeout);
        var disposal = generation.DisposeAsync().AsTask();

        try
        {
            Assert.False(disposal.IsCompleted);
            Assert.False(instance.IsDisposed);
            Assert.Equal(0, api.DisposeCallCount);
        }
        finally
        {
            release.TrySetResult();
        }

        await Task.WhenAll(cancel, disposal).WaitAsync(TestTimeout);
        Assert.True(instance.IsDisposed);
        Assert.Equal(1, api.DisposeCallCount);
    }

    [Fact]
    public async Task A_failing_instance_disposal_does_not_skip_remaining_resources()
    {
        using var manager = CreateManager();
        var broken = new ThrowingDuoInstance();
        var other = CreateInstance("generation", "Other");
        var api = new ControlledDuoAPI();
        manager.EnqueueGeneration(api, broken, other);
        await manager.StartGeneration(101);

        await manager.StopGeneration().WaitAsync(TestTimeout);

        Assert.True(other.IsDisposed);
        Assert.Equal(1, api.DisposeCallCount);
        Assert.Empty(manager);
    }

    private sealed class ThrowingDuoInstance() : DuoInstance(new DuoInstanceInfo { Name = "Broken" }, CreateSettings("Broken"))
    {
        public override void Dispose()
        {
            base.Dispose();
            throw new InvalidOperationException("Test disposal failure");
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
