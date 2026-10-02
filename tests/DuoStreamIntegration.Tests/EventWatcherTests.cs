using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

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
    public async Task Overlapping_names_request_a_refresh_instead_of_guessing(
        string leftName, string leftDisplayName, string rightName, string rightDisplayName, bool reverseOrder)
    {
        using var alpha = new DuoInstance(leftName, Settings(leftName) with { DisplayName = leftDisplayName }, new() { Name = leftName });
        using var beta = new DuoInstance(rightName, Settings(rightName) with { DisplayName = rightDisplayName }, new() { Name = rightName });
        using var watcher = new TestEventWatcher { Logger = NullLogger.Instance };
        await using var run = new WatchRun(watcher, reverseOrder ? [beta, alpha] : [alpha, beta]);

        Assert.True(watcher.Publish(DuoEventID.InstanceStarted, beta.Name));
        var signal = await run.Next();
        Assert.Null(signal.Instance);
        Assert.Null(signal.IsRunning);
        Assert.Null(alpha.Session);
        Assert.Null(beta.Session);
    }

    [Theory]
    [InlineData((int)DuoEventID.InstanceStarted, true)]
    [InlineData((int)DuoEventID.InstanceStopped, false)]
    [InlineData((int)DuoEventID.InstanceError, false)]
    public async Task Distinct_names_publish_direct_state_signals(int id, bool running)
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        using var watcher = new TestEventWatcher { Logger = NullLogger.Instance };
        await using var run = new WatchRun(watcher, alpha, beta);

        watcher.Publish((DuoEventID)id, beta.Name);
        var signal = await run.Next();
        Assert.Same(beta, signal.Instance);
        Assert.Equal(running, signal.IsRunning);
        Assert.Null(beta.IsRunning); // State application belongs to the context.
    }

    [Fact]
    public async Task Resume_requests_a_refresh_of_all_instances()
    {
        using var watcher = new TestEventWatcher { Logger = NullLogger.Instance };
        await using var run = new WatchRun(watcher);
        watcher.Publish(DuoEventID.Resuming);
        var signal = await run.Next();
        Assert.Null(signal.Instance);
        Assert.Null(signal.IsRunning);
    }

    [Fact]
    public async Task Cancellation_unsubscribes_and_stops_an_idle_reader()
    {
        using var watcher = new TestEventWatcher { Logger = NullLogger.Instance };
        using var cancellation = new CancellationTokenSource();
        await using var reader = ((IDuoWatcher)watcher).WatchAsync([], cancellation.Token).GetAsyncEnumerator();
        var next = reader.MoveNextAsync().AsTask();
        await watcher.Subscribed.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(TestTimeout));
        Assert.True(watcher.Unsubscribed.Task.IsCompleted);
        Assert.False(watcher.Publish(DuoEventID.Resuming));
    }

    [Fact]
    public async Task Callback_disposes_records_including_ignored_and_unmatched_events()
    {
        using var instance = Instance();
        using var watcher = new TestEventWatcher { Logger = NullLogger.Instance };
        await using var run = new WatchRun(watcher, instance);
        watcher.Publish(DuoEventID.InstanceStarted, instance.Name);
        watcher.Publish(DuoEventID.ServiceStarted);
        watcher.Publish(DuoEventID.InstanceStarted, "Unknown");

        Assert.Same(instance, (await run.Next()).Instance);
        Assert.Equal(3, watcher.DeliveredRecords.Count);
        Assert.All(watcher.DeliveredRecords, record => Assert.True(record.Disposed));
        Assert.False(run.TryRead(out _));
    }

    [Fact]
    public async Task Read_errors_and_unmatched_events_do_not_stop_later_notifications()
    {
        using var instance = Instance();
        var logger = new RecordingLogger();
        using var watcher = new TestEventWatcher { Logger = logger };
        await using var run = new WatchRun(watcher, instance);
        var error = new InvalidOperationException("Event log unavailable");
        watcher.PublishError(error);
        watcher.Publish(DuoEventID.InstanceStarted, "Unknown");
        watcher.Publish(DuoEventID.InstanceStarted, instance.Name);

        Assert.True((await run.Next()).IsRunning);
        Assert.Equal(2, logger.Errors.Count);
        Assert.Contains(error, logger.Errors);
    }

    [Fact]
    public async Task Direct_notifications_preserve_their_order()
    {
        using var instance = Instance();
        using var watcher = new TestEventWatcher { Logger = NullLogger.Instance };
        await using var run = new WatchRun(watcher, instance);
        watcher.Publish(DuoEventID.InstanceStarted, instance.Name);
        watcher.Publish(DuoEventID.InstanceStopped, instance.Name);

        Assert.True((await run.Next()).IsRunning);
        Assert.False((await run.Next()).IsRunning);
    }

    [Fact]
    public async Task A_raw_event_delegate_can_propagate_an_error_to_the_trigger_caller()
    {
        using var instance = Instance();
        instance.Started += _ => Task.FromException(new InvalidOperationException("Raw delegate failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IEventSystem)instance)[nameof(DuoInstance.Started)].TriggerEventAsync());
    }

    [Fact]
    public async Task Configured_action_errors_are_handled_without_stopping_state_updates()
    {
        using var instance = new ErrorHandlingInstance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        ((IEventSystem)instance)[nameof(DuoInstance.Started)].AddAction(new JSEventAction("test-failure"));
        await StartContext(context);

        await watcher.SignalAsync(instance, true);
        instance.Release.TrySetResult();
        await instance.ErrorHandled.Task.WaitAsync(TestTimeout);
        await watcher.SignalAsync(instance, false);

        Assert.False(instance.IsRunning);
        Assert.Single(instance.Errors);
    }
}

internal sealed class ErrorHandlingInstance() : DuoInstance("Player", Settings(), new() { Name = "Player" })
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
