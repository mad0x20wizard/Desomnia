using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class DuoConcurrencyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Same_instance_commands_are_serialized(bool secondRunning)
    {
        using var instance = Instance();
        var entered = Signal();
        var release = Signal();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await context.StartWatching(TestTimeout);
        manager.OnChange = async (target, running, token) =>
        {
            if (running)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            watcher.Publish(target, running);
        };
        var first = context.Start(instance, TestTimeout);
        await entered.Task.WaitAsync(TestTimeout);
        var second = secondRunning ? context.Start(instance, TestTimeout) : context.Stop(instance, TestTimeout);
        try
        {
            Assert.False(second.IsCompleted);
            Assert.Equal(1, manager.Starts);
            Assert.Equal(0, manager.Stops);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(TestTimeout);
        Assert.Equal(secondRunning, (instance.Session is not null));
        Assert.Equal(1, manager.Starts);
        Assert.Equal(secondRunning ? 0 : 1, manager.Stops);
    }

    [Fact]
    public async Task Different_instances_can_start_concurrently()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var bothEntered = Signal();
        var entered = 0;
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, alpha, beta);
        await context.StartWatching(TestTimeout);
        manager.OnChange = async (target, running, token) =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
            await bothEntered.Task.WaitAsync(token);
            watcher.Publish(target, running);
        };
        await Task.WhenAll(context.Start(alpha, TestTimeout), context.Start(beta, TestTimeout)).WaitAsync(TestTimeout);
        Assert.NotNull(alpha.Session);
        Assert.NotNull(beta.Session);
        Assert.Equal(2, manager.Starts);
    }

    [Fact]
    public async Task State_is_visible_before_command_completion_releases_the_next_command()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await context.StartWatching(TestTimeout);
        var started = context.Start(instance, TestTimeout);
        Task? stopped = null;
        watcher.SessionChanged += (_, args) =>
        {
            if (args.Session is null) return;
            // Hold notification dispatch while the command resumes on another thread.
            started.WaitAsync(TestTimeout).GetAwaiter().GetResult();
            stopped = context.Stop(instance, TestTimeout);
        };
        await Task.Run(() => watcher.Publish(instance, true)).WaitAsync(TestTimeout);
        Assert.Equal(1, manager.Stops);
        watcher.Publish(instance, false);
        await stopped!.WaitAsync(TestTimeout);
        Assert.Null(instance.Session);
    }

    [Fact]
    public async Task Context_disposal_cancels_in_flight_and_queued_commands()
    {
        using var instance = Instance();
        var entered = Signal();
        var manager = new ControlledManager
        {
            OnChange = async (_, _, token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        using var context = Context(manager, Watcher(manager), instance);
        await context.StartWatching(TestTimeout);
        var first = context.Start(instance, TestTimeout);
        await entered.Task.WaitAsync(TestTimeout);
        var queued = context.Start(instance, TestTimeout);
        ((IDisposable)context).Dispose();
        await Task.WhenAll(first, queued).WaitAsync(TestTimeout);
        Assert.Equal(1, manager.Starts);
    }
}
