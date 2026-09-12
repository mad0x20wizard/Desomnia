using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using Microsoft.Extensions.Logging.Abstractions;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo.Configuration;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;
using MadWizard.Desomnia.Service.Duo;

namespace DuoStreamIntegration.Tests;

public sealed class EventWatcherTests
{
    [Theory]
    [InlineData("Gaming", "Main", "Gaming Guest", "Guest", false)]
    [InlineData("Gaming", "Main", "Guest", "Gaming Guest", false)]
    [InlineData("Main", "Gaming", "Gaming Guest", "Guest", false)]
    [InlineData("Main", "Gaming", "Guest", "Gaming Guest", false)]
    [InlineData("Gaming", "Main", "Gaming Guest", "Guest", true)]
    [InlineData("Gaming", "Main", "Guest", "Gaming Guest", true)]
    [InlineData("Main", "Gaming", "Gaming Guest", "Guest", true)]
    [InlineData("Main", "Gaming", "Guest", "Gaming Guest", true)]
    [InlineData("Gaming", "Main", "Guest", "Gaming", false)]
    public async Task Overlapping_names_and_display_names_refresh_instead_of_updating_the_wrong_instance(
        string leftName, string leftDisplayName, string rightName, string rightDisplayName, bool reverseOrder)
    {
        using var alpha = new DuoInstance(leftName, Settings(leftName) with { DisplayName = leftDisplayName }, new() { Name = leftName });
        using var beta = new DuoInstance(rightName, Settings(rightName) with { DisplayName = rightDisplayName }, new() { Name = rightName });
        var manager = new ControlledManager { OnQuery = (instance, _) => Task.FromResult(instance == beta) };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync(reverseOrder ? [beta, alpha] : [alpha, beta], lifetime.Token);
        try
        {
            await watcher.SendAsync(DuoEventID.InstanceStarted, $"{beta.Name} ({beta.Settings.DisplayName})").WaitAsync(TestTimeout);
            Assert.False(alpha.IsRunning);
            Assert.True(beta.IsRunning);
            Assert.Equal(2, manager.Queries);
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task Distinct_instances_keep_the_fast_path_when_their_own_name_equals_their_display_name()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var manager = new ControlledManager();
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([alpha, beta], lifetime.Token);
        try
        {
            await watcher.SendAsync(DuoEventID.InstanceStarted, beta.Name).WaitAsync(TestTimeout);
            Assert.False(alpha.IsRunning);
            Assert.True(beta.IsRunning);
            Assert.Equal(0, manager.Queries);
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact, Trait("Issue", "3")]
    public async Task Cancellation_between_buffered_direct_updates_stops_before_the_next_update()
    {
        using var instance = Instance();
        var watcher = new TestEventWatcher { Manager = new ControlledManager(), Logger = NullLogger.Instance };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var changes = 0;
        watcher.StatusChanged += (_, args) =>
        {
            changes++;
            if (args.Status)
            {
                watcher.Publish(DuoEventID.InstanceStopped, instance.Name);
                lifetime.Cancel();
            }
        };
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        watcher.Publish(DuoEventID.InstanceStarted, instance.Name);
        await watching.WaitAsync(TestTimeout);
        Assert.Equal(1, changes);
        Assert.True(instance.IsRunning);
    }

    [Fact, Trait("Issue", "2")]
    public async Task Callback_disposes_delivered_records_including_ignored_and_unmatched_events()
    {
        using var instance = Instance();
        var watcher = new TestEventWatcher { Manager = new ControlledManager(), Logger = NullLogger.Instance };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            await watcher.SendAsync(DuoEventID.InstanceStarted, instance.Name).WaitAsync(TestTimeout);
            await watcher.SendAsync(DuoEventID.ServiceStarted).WaitAsync(TestTimeout);
            await watcher.SendAsync(DuoEventID.InstanceStarted, "Unknown").WaitAsync(TestTimeout);
            Assert.Equal(3, watcher.DeliveredRecords.Count);
            Assert.All(watcher.DeliveredRecords, record => Assert.True(record.Disposed,
                "The callback owns each delivered EventRecord and must dispose it."));
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task Event_log_read_errors_are_logged()
    {
        var logger = new RecordingLogger();
        var watcher = new TestEventWatcher { Manager = new ControlledManager(), Logger = logger };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([], lifetime.Token);
        try
        {
            var error = new InvalidOperationException("Event log unavailable");
            watcher.PublishError(error);
            Assert.Same(error, Assert.Single(logger.Errors));
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact, Trait("Issue", "3")]
    public async Task Request_timeout_does_not_stop_watching_when_lifetime_is_not_canceled()
    {
        using var instance = Instance();
        var manager = new ControlledManager
        {
            // HttpClient can time out independently of the watcher lifetime token.
            OnQuery = (_, _) => Task.FromException<bool>(new TaskCanceledException("HTTP request timed out"))
        };
        var logger = new RecordingLogger();
        var watcher = new TestEventWatcher { Manager = manager, Logger = logger };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var refresh = watcher.SendAsync(DuoEventID.Resuming);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            await refresh.WaitAsync(TestTimeout);
            Assert.False(watching.IsCompleted, "A request timeout terminated the watcher even though its lifetime was not canceled.");
            await watcher.SendAsync(DuoEventID.InstanceStarted, instance.Name).WaitAsync(TestTimeout);
            Assert.True(instance.IsRunning);
            Assert.Single(logger.Errors);
        }
        finally
        {
            lifetime.Cancel();
            try { await watching.WaitAsync(TestTimeout); }
            catch (OperationCanceledException) { } // The assertion above reports this regression.
        }
    }

    [Fact]
    public async Task A_raw_event_delegate_can_propagate_an_error_to_the_trigger_caller()
    {
        using var instance = Instance();
        instance.Started += _ => Task.FromException(new InvalidOperationException("Raw delegate failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IEventSystem)instance)[nameof(DuoInstance.Started)].TriggerEventAsync());
    }

    [Fact, Trait("Issue", "3")]
    public async Task Stop_notification_is_applied_after_an_earlier_pending_refresh()
    {
        using var instance = Instance();
        instance.IsRunning = true;
        var queryEntered = Signal();
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new ControlledManager
        {
            OnQuery = (_, token) => { queryEntered.TrySetResult(); return reply.Task.WaitAsync(token); }
        };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        // Both records are available synchronously. A consumer that starts work without
        // awaiting it would process Stop before WatchAsync returns to the test.
        var refresh = watcher.SendAsync(DuoEventID.Resuming);
        var stop = watcher.SendAsync(DuoEventID.InstanceStopped, instance.Name);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            await watcher.Subscribed.Task.WaitAsync(TestTimeout);
            await queryEntered.Task.WaitAsync(TestTimeout);
            Assert.False(stop.IsCompleted);
            reply.SetResult(true);
            await Task.WhenAll(refresh, stop).WaitAsync(TestTimeout);
            Assert.False(instance.IsRunning);
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact, Trait("Issue", "3")]
    public async Task Two_refresh_notifications_do_not_query_concurrently()
    {
        using var instance = Instance();
        var release = Signal();
        var active = 0;
        var overlap = false;
        var manager = new ControlledManager
        {
            OnQuery = async (_, token) =>
            {
                if (Interlocked.Increment(ref active) > 1) overlap = true;
                try { await release.Task.WaitAsync(token); return true; }
                finally { Interlocked.Decrement(ref active); }
            }
        };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        var first = watcher.SendAsync(DuoEventID.Resuming);
        var second = watcher.SendAsync(DuoEventID.Resuming);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            Assert.Equal(1, manager.Queries);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TestTimeout);
            Assert.False(overlap);
            Assert.Equal(2, manager.Queries);
        }
        finally
        {
            release.TrySetResult();
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact, Trait("Issue", "3")]
    public async Task A_query_returning_after_cancellation_cannot_publish_state()
    {
        using var instance = Instance();
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new ControlledManager { OnQuery = (_, _) => reply.Task };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        _ = watcher.SendAsync(DuoEventID.Resuming);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        lifetime.Cancel();
        reply.SetResult(true);
        await watching.WaitAsync(TestTimeout);
        Assert.False(instance.IsRunning);
        Assert.True(watcher.Unsubscribed.Task.IsCompleted);
    }

    [Fact, Trait("Issue", "3")]
    public async Task Configured_action_errors_are_handled_by_the_event_engine()
    {
        using var instance = new ErrorHandlingInstance();
        var logger = new RecordingLogger();
        ((IEventSystem)instance)[nameof(DuoInstance.Started)].AddAction(new JSEventAction("test-failure"));
        var watcher = new TestEventWatcher { Manager = new ControlledManager(), Logger = logger };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            await watcher.SendAsync(DuoEventID.InstanceStarted, instance.Name).WaitAsync(TestTimeout);
            instance.Release.TrySetResult();
            await instance.ErrorHandled.Task.WaitAsync(TestTimeout);
            await watcher.SendAsync(DuoEventID.InstanceStopped, instance.Name).WaitAsync(TestTimeout);
            Assert.False(instance.IsRunning);
            Assert.Single(instance.Errors);
            Assert.Empty(logger.Errors);
        }
        finally
        {
            instance.Release.TrySetResult();
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact, Trait("Issue", "3")]
    public async Task Context_disposal_waits_for_the_active_refresh_and_unsubscribes()
    {
        using var instance = Instance();
        var queryEntered = Signal();
        var queryCanceled = Signal();
        var releaseQuery = Signal();
        var calls = 0;
        var manager = new ControlledManager
        {
            OnQuery = async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1) return false;
                queryEntered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally
                {
                    queryCanceled.TrySetResult();
                    await releaseQuery.Task;
                }
                return true;
            }
        };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        using var context = Context(manager, watcher, instance);
        context.StartWatching(TestTimeout);
        await watcher.Subscribed.Task.WaitAsync(TestTimeout);
        _ = watcher.SendAsync(DuoEventID.Resuming);
        await queryEntered.Task.WaitAsync(TestTimeout);
        var disposal = Task.Run(() => ((IDisposable)context).Dispose());
        try
        {
            await queryCanceled.Task.WaitAsync(TestTimeout);
            Assert.False(disposal.IsCompleted);
            Assert.False(watcher.Unsubscribed.Task.IsCompleted);
            _ = watcher.SendAsync(DuoEventID.InstanceStarted, instance.Name);
        }
        finally
        {
            releaseQuery.TrySetResult();
            await disposal.WaitAsync(TestTimeout);
        }
        Assert.True(watcher.Unsubscribed.Task.IsCompleted);
        Assert.False(instance.IsRunning);
        Assert.False(watcher.Publish(DuoEventID.InstanceStarted, instance.Name));
    }

    [Fact, Trait("Issue", "3")]
    public async Task Started_action_can_await_stop_without_blocking_the_watcher()
    {
        using var instance = Instance();
        var stopRequested = Signal();
        var actionCompleted = Signal();
        var manager = new ControlledManager
        {
            OnChange = (_, running, _) =>
            {
                if (!running) stopRequested.TrySetResult();
                return Task.CompletedTask;
            }
        };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        using var context = Context(manager, watcher, instance);
        context.StartWatching(TestTimeout);
        instance.Started += async _ =>
        {
            await context.Stop(instance, TestTimeout);
            actionCompleted.TrySetResult();
        };

        await watcher.SendAsync(DuoEventID.InstanceStarted, instance.Name).WaitAsync(TestTimeout);
        await stopRequested.Task.WaitAsync(TestTimeout);
        Assert.False(actionCompleted.Task.IsCompleted);
        await watcher.SendAsync(DuoEventID.InstanceStopped, instance.Name).WaitAsync(TestTimeout);
        await actionCompleted.Task.WaitAsync(TestTimeout);
        Assert.False(instance.IsRunning);
    }

    [Theory, Trait("Issue", "3")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_notification_does_not_stop_later_notifications(bool failedQuery)
    {
        using var instance = Instance();
        var manager = new ControlledManager { OnQuery = (_, _) => throw new HttpRequestException("Offline") };
        var logger = new RecordingLogger();
        var watcher = new TestEventWatcher { Manager = manager, Logger = logger };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            await watcher.SendAsync(failedQuery ? DuoEventID.Resuming : DuoEventID.InstanceStarted, "Unknown").WaitAsync(TestTimeout);
            await watcher.SendAsync(DuoEventID.InstanceStarted, instance.Name).WaitAsync(TestTimeout);
            Assert.True(instance.IsRunning);
            Assert.Single(logger.Errors);
            Assert.False(watching.IsCompleted);
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task Ambiguous_instance_names_refresh_states_instead_of_guessing()
    {
        using var alpha = Instance("Player");
        using var beta = Instance("PlayerTwo");
        var manager = new ControlledManager { OnQuery = (instance, _) => Task.FromResult(instance == beta) };
        var watcher = new TestEventWatcher { Manager = manager, Logger = NullLogger.Instance };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([alpha, beta], lifetime.Token);
        try
        {
            await watcher.SendAsync(DuoEventID.InstanceStarted, beta.Name).WaitAsync(TestTimeout);
            Assert.False(alpha.IsRunning);
            Assert.True(beta.IsRunning);
            Assert.Equal(2, manager.Queries);
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }
}

internal sealed class ErrorHandlingInstance() : DuoInstance("Player", Settings(), new DuoInstanceWatchInfo { Name = "Player" })
{
    public TaskCompletionSource Release { get; } = Signal();
    public TaskCompletionSource ErrorHandled { get; } = Signal();
    public List<ActionError> Errors { get; } = [];

    [ActionHandler("test-failure")]
    private async Task Fail()
    {
        await Release.Task;
        throw new InvalidOperationException("Action failed");
    }

    protected override bool OnActionError(ActionError error)
    {
        Errors.Add(error);
        ErrorHandled.TrySetResult();
        return true;
    }
}
