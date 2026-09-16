using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class DuoServiceContextTests
{
    [Fact]
    public void Startup_initializes_all_instances_and_disposal_stops_the_watcher()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var manager = new ControlledManager { OnQuery = (instance, _) => Task.FromResult(instance == alpha) };
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, alpha, beta);
        context.StartWatching(TestTimeout);

        Assert.True(alpha.IsRunning);
        Assert.False(beta.IsRunning);
        Assert.Equal(2, manager.Queries);
        Assert.True(watcher.Started);
        ((IDisposable)context).Dispose();
        Assert.True(watcher.Stopped);
    }

    [Fact]
    public async Task Command_observes_a_notification_published_inside_the_API_call()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        manager.OnChange = (target, running, _) =>
        {
            watcher.Publish(target, running);
            return Task.CompletedTask;
        };
        bool? manual = null;
        watcher.StatusChanged += (_, args) => manual = args.Manually;

        await context.Start(instance, TestTimeout);
        Assert.True(instance.IsRunning);
        // A later external transition has no command waiter left.
        watcher.Publish(instance, false);
        Assert.True(manual);
        Assert.Equal(1, manager.Starts);
    }

    [Fact]
    public async Task An_instance_from_another_context_cannot_use_this_API()
    {
        using var current = Instance();
        using var old = Instance();
        var manager = new ControlledManager();
        using var context = Context(manager, Watcher(manager), current);
        await context.Start(old, TestTimeout);
        Assert.Equal(0, manager.Starts);
    }

    [Fact]
    public async Task Timeout_removes_waiter_and_allows_a_later_command()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await Assert.ThrowsAsync<TimeoutException>(() => context.Start(instance, TimeSpan.FromMilliseconds(50)));
        bool? manual = null;
        watcher.StatusChanged += (_, args) => manual = args.Manually;
        watcher.Publish(instance, true);
        Assert.True(manual);
        watcher.Publish(instance, false);
        manager.OnChange = (target, running, _) =>
        {
            watcher.Publish(target, running);
            return Task.CompletedTask;
        };
        await context.Start(instance, TestTimeout);
        Assert.True(instance.IsRunning);
        Assert.Equal(2, manager.Starts);
    }

    [Fact]
    public async Task Timeout_while_waiting_for_mutex_does_not_call_the_API()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        using var context = Context(manager, Watcher(manager), instance);
        using (await instance.Mutex.LockAsync())
        {
            await Assert.ThrowsAsync<TimeoutException>(() => context.Start(instance, TimeSpan.FromMilliseconds(50)));
        }
        Assert.Equal(0, manager.Starts);
    }

    [Fact]
    public async Task API_failure_releases_the_instance_mutex_and_notification_waiter()
    {
        using var instance = Instance();
        var manager = new ControlledManager { OnChange = (_, _, _) => throw new HttpRequestException("Offline") };
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => context.Start(instance, TestTimeout));
        bool? manual = null;
        watcher.StatusChanged += (_, args) => manual = args.Manually;
        watcher.Publish(instance, true);
        Assert.True(manual);
        manager.OnChange = (target, running, _) =>
        {
            watcher.Publish(target, running);
            return Task.CompletedTask;
        };
        await context.Stop(instance, TestTimeout);
        Assert.False(instance.IsRunning);
    }
}
