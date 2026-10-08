using MadWizard.Desomnia.Service.Duo;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class DuoServiceContextTests
{
    [Fact]
    public async Task Initialization_queries_running_state_without_creating_sessions_or_raising_actions()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var manager = new ControlledManager { OnQuery = (instance, _) => Task.FromResult(instance == alpha) };
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, alpha, beta);
        var events = 0;
        alpha.Started += _ => { events++; return Task.CompletedTask; };
        beta.Stopped += _ => { events++; return Task.CompletedTask; };

        await StartContext(context);

        Assert.True(alpha.IsRunning);
        Assert.False(beta.IsRunning);
        Assert.Null(alpha.Session);
        Assert.Null(beta.Session);
        Assert.Equal(0, events);
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
        await StartContext(context);
        manager.OnChange = (target, running, _) => watcher.PublishAsync(target, running);

        await context.Start(instance, TestTimeout);
        Assert.True(instance.IsRunning);
        Assert.NotNull(instance.Session);
        await watcher.PublishAsync(instance, false);
        Assert.False(instance.IsRunning);
        Assert.Equal(1, manager.Starts);
    }

    [Fact]
    public async Task An_instance_from_another_context_cannot_use_this_API()
    {
        using var current = Instance();
        using var old = Instance();
        var manager = new ControlledManager();
        using var context = Context(manager, Watcher(manager), current);
        await StartContext(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Start(old, TestTimeout));
        Assert.Equal(0, manager.Starts);
    }

    [Fact]
    public async Task Timeout_removes_waiter_and_allows_a_later_command()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await StartContext(context);
        await Assert.ThrowsAsync<TimeoutException>(() => context.Start(instance, TimeSpan.FromMilliseconds(50)));

        await watcher.PublishAsync(instance, true);
        await watcher.PublishAsync(instance, false);
        manager.OnChange = (target, running, _) => watcher.PublishAsync(target, running);
        await context.Start(instance, TestTimeout);

        Assert.True(instance.IsRunning);
        Assert.NotNull(instance.Session);
        Assert.Equal(2, manager.Starts);
    }

    [Fact]
    public async Task Timeout_while_waiting_for_mutex_does_not_call_the_API()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        using var context = Context(manager, Watcher(manager), instance);
        await StartContext(context);
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
        await StartContext(context);
        await Assert.ThrowsAsync<HttpRequestException>(() => context.Start(instance, TestTimeout));

        await watcher.PublishAsync(instance, true);
        manager.OnChange = (target, running, _) => watcher.PublishAsync(target, running);
        await context.Stop(instance, TestTimeout);
        Assert.False(instance.IsRunning);
        Assert.Null(instance.Session);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commands_wait_until_backend_and_session_both_match(bool running)
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        manager.SetState(instance, !running);
        ControlledWatcher.SetSession(instance, running ? null : SessionFor(instance));
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await StartContext(context);
        var entered = Signal();
        manager.OnChange = (_, _, _) => { entered.TrySetResult(); return Task.CompletedTask; };
        var command = running ? context.Start(instance, TestTimeout) : context.Stop(instance, TestTimeout);
        await entered.Task.WaitAsync(TestTimeout);

        // In particular, a logout gap must not complete Stop while Duo remains running.
        ControlledWatcher.SetSession(instance, running ? SessionFor(instance) : null);
        await watcher.SignalAsync(instance, !running);
        Assert.False(command.IsCompleted);

        // A backend transition alone is insufficient while the session still disagrees.
        ControlledWatcher.SetSession(instance, running ? null : SessionFor(instance));
        manager.SetState(instance, running);
        await watcher.SignalAsync(instance, running);
        Assert.False(command.IsCompleted);

        ControlledWatcher.SetSession(instance, running ? SessionFor(instance) : null);
        await watcher.SignalAsync(instance);
        await command.WaitAsync(TestTimeout);
        Assert.Equal(running, instance.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_failures_are_logged_and_later_signals_are_processed(bool timeout)
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        var logger = new RecordingLogger<DuoServiceContext>();
        using var context = new DuoServiceContext
        {
            Manager = manager, Watcher = watcher, Instances = [instance], Logger = logger
        };
        await StartContext(context);
        manager.OnQuery = (_, _) => throw (timeout
            ? new TaskCanceledException("Request timed out")
            : new HttpRequestException("Offline"));
        await watcher.SignalAsync(instance);
        Assert.Single(logger.Errors);
        Assert.False(instance.IsRunning);

        manager.OnQuery = (_, _) => Task.FromResult(true);
        await watcher.SignalAsync(instance);
        Assert.False(instance.IsRunning); // A backend start alone does not complete the transition.
        Assert.Null(instance.Session);
        ControlledWatcher.SetSession(instance, SessionFor(instance));
        await watcher.SignalAsync(instance);
        Assert.True(instance.IsRunning);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task Queries_and_direct_signals_are_processed_in_timeline_order()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await StartContext(context);
        var entered = Signal();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnQuery = (_, token) => { entered.TrySetResult(); return release.Task.WaitAsync(token); };
        var first = watcher.SignalAsync(instance);
        await entered.Task.WaitAsync(TestTimeout);
        var second = watcher.SignalAsync(instance);
        var stopped = watcher.SignalAsync(instance, false);
        Assert.False(second.IsCompleted);
        Assert.False(stopped.IsCompleted);
        Assert.Equal(2, manager.Queries); // Initialization plus the first refresh.
        release.TrySetResult(true);
        await Task.WhenAll(first, second, stopped).WaitAsync(TestTimeout);
        Assert.Equal(3, manager.Queries);
        Assert.False(instance.IsRunning);
    }

    [Fact]
    public async Task Started_action_can_await_stop_without_blocking_the_watcher()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await StartContext(context);
        var requested = Signal();
        var completed = Signal();
        manager.OnChange = (_, _, _) => { requested.TrySetResult(); return Task.CompletedTask; };
        instance.Started += async _ =>
        {
            await context.Stop(instance, TestTimeout);
            completed.TrySetResult();
        };

        await watcher.PublishAsync(instance, true);
        await requested.Task.WaitAsync(TestTimeout);
        Assert.False(completed.Task.IsCompleted);
        await watcher.PublishAsync(instance, false);
        await completed.Task.WaitAsync(TestTimeout);
        Assert.False(instance.IsRunning);
    }

    [Fact]
    public async Task Repeated_state_signals_do_not_repeat_actions()
    {
        using var instance = Instance();
        var manager = new ControlledManager();
        var watcher = Watcher(manager);
        using var context = Context(manager, watcher, instance);
        await StartContext(context);
        var starts = 0;
        var stops = 0;
        instance.Started += _ => { starts++; return Task.CompletedTask; };
        instance.Stopped += _ => { stops++; return Task.CompletedTask; };

        await watcher.PublishAsync(instance, true);
        await watcher.SignalAsync(instance, true);
        await watcher.PublishAsync(instance, false);
        await watcher.SignalAsync(instance, false);

        Assert.Equal(1, starts);
        Assert.Equal(1, stops);
    }
}
