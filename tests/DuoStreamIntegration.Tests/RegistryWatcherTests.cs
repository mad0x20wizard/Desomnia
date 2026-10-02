using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using System.Threading.Channels;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class RegistryWatcherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Unrelated client name")]
    public async Task Adding_and_removing_the_value_publishes_running_and_refresh_signals(string? clientName)
    {
        using var instance = Instance();
        var session = new FakeSession(42, clientName);
        using var registry = new InstanceRegistry();
        var watcher = registry.Watcher(session);
        await using var run = new WatchRun(watcher, instance);
        Assert.Null(instance.Session);

        registry.Key.SetValue("SessionId", 42);
        var started = await run.Next();
        Assert.Same(instance, started.Instance);
        Assert.True(started.IsRunning);
        Assert.Same(session, instance.Session);

        registry.Key.DeleteValue("SessionId");
        var refresh = await run.Next();
        Assert.Same(instance, refresh.Instance);
        Assert.Null(refresh.IsRunning); // Absence of a session does not imply Duo has stopped.
        await run.DisposeAsync();
        Assert.Equal(0, session.LogoffSubscribers);
    }

    [Fact]
    public async Task Existing_session_is_bound_before_waiting_for_changes()
    {
        using var instance = Instance();
        var session = new FakeSession(42, null);
        using var registry = new InstanceRegistry();
        registry.Key.SetValue("SessionId", 42);
        await using var run = new WatchRun(registry.Watcher(session), instance);

        Assert.Same(session, instance.Session);
        Assert.Equal(1, session.LogoffSubscribers);
        Assert.False(run.TryRead(out _)); // Context initialization queries running state separately.
    }

    [Fact]
    public async Task Logoff_removes_the_retained_value_and_requests_a_backend_refresh()
    {
        using var instance = Instance();
        var session = new FakeSession(42, null);
        using var registry = new InstanceRegistry();
        registry.Key.SetValue("SessionId", 42);
        await using var run = new WatchRun(registry.Watcher(session), instance);

        session.RaiseLoggedOff();
        var signal = await run.Next();

        Assert.Same(instance, signal.Instance);
        Assert.Null(signal.IsRunning);
        Assert.Null(registry.Key.GetValue("SessionId"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("another-user")]
    public async Task Startup_clears_stale_ids_and_preserves_matching_sessions(string? staleUser)
    {
        using var stale = Instance();
        using var active = Instance("Active");
        var valid = new FakeSession(43, null, "PLAYER");
        using var registry = new InstanceRegistry();
        using var activeKey = registry.CreateKey("Active");
        registry.Key.SetValue("SessionId", 42);
        activeKey.SetValue("SessionId", 43);
        var sessions = new List<FakeSession> { valid };
        if (staleUser is not null) sessions.Add(new FakeSession(42, null, staleUser));
        await using var run = new WatchRun(registry.Watcher([.. sessions]), stale, active);

        Assert.Null(registry.Key.GetValue("SessionId"));
        Assert.Null(stale.Session);
        Assert.Equal(43, activeKey.GetValue("SessionId"));
        Assert.Same(valid, active.Session);
    }

    [Fact]
    public async Task Two_instances_with_the_same_user_bind_by_their_distinct_session_ids()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var first = new FakeSession(42, null);
        var second = new FakeSession(43, null);
        using var registry = new InstanceRegistry();
        using var alphaKey = registry.CreateKey("Alpha");
        using var betaKey = registry.CreateKey("Beta");
        alphaKey.SetValue("SessionId", 42);
        betaKey.SetValue("SessionId", 43);
        await using var run = new WatchRun(registry.Watcher(first, second), alpha, beta);

        Assert.Same(first, alpha.Session);
        Assert.Same(second, beta.Session);
        first.RaiseLoggedOff();
        var signal = await run.Next();
        Assert.Same(alpha, signal.Instance);
        Assert.Null(signal.IsRunning);
        Assert.Null(alphaKey.GetValue("SessionId"));
        Assert.Equal(43, betaKey.GetValue("SessionId"));
    }

    [Fact]
    public async Task Invalid_update_is_logged_and_a_later_valid_update_is_processed()
    {
        using var instance = Instance();
        var wrong = new FakeSession(42, null, "someone-else");
        var valid = new FakeSession(43, null);
        using var registry = new InstanceRegistry();
        var logger = new RecordingLogger<RegistryWatcher>();
        registry.AddSession(wrong);
        registry.AddSession(valid);
        var watcher = new RegistryWatcher { SessionMonitor = registry.Monitor, Logger = logger };
        await using var run = new WatchRun(watcher, instance);
        registry.Key.SetValue("SessionId", 42);
        await logger.ErrorReported.Task.WaitAsync(TestTimeout);
        Assert.Null(instance.Session);
        Assert.False(run.TryRead(out _));

        registry.Key.SetValue("SessionId", 43);
        Assert.True((await run.Next()).IsRunning);
        Assert.Same(valid, instance.Session);
    }

    [Fact]
    public async Task A_session_is_known_before_Duo_publishes_its_id()
    {
        using var instance = Instance();
        using var registry = new InstanceRegistry();
        await using var run = new WatchRun(registry.Watcher(), instance);
        var session = new FakeSession(42, null);
        registry.AddSession(session);
        registry.Key.SetValue("SessionId", 42);

        Assert.True((await run.Next()).IsRunning);
        Assert.Same(session, instance.Session);
        Assert.Equal(1, session.LogoffSubscribers);
    }

    [Fact]
    public async Task Logoff_of_previous_session_does_not_delete_a_replacement_id()
    {
        using var instance = Instance();
        var first = new FakeSession(42, null);
        var second = new FakeSession(43, null);
        using var registry = new InstanceRegistry();
        registry.Key.SetValue("SessionId", 42);
        await using var run = new WatchRun(registry.Watcher(first, second), instance);
        instance.StopTracking<SessionWatch>(); // SessionWatchAdapter detaches the old watch on logout.
        registry.Key.SetValue("SessionId", 43);
        first.RaiseLoggedOff();

        Assert.True((await run.Next()).IsRunning);
        Assert.Equal(43, registry.Key.GetValue("SessionId"));
        Assert.Same(second, instance.Session);
        Assert.Equal(0, first.LogoffSubscribers);
        Assert.Equal(1, second.LogoffSubscribers);
    }

    [Fact]
    public async Task Context_commands_use_registry_signals_and_query_backend_state()
    {
        using var instance = Instance();
        var session = new FakeSession(42, null);
        using var registry = new InstanceRegistry();
        var watcher = registry.Watcher(session);
        var manager = new ControlledManager();
        manager.OnChange = (target, running, _) =>
        {
            manager.SetState(target, running);
            if (running) registry.Key.SetValue("SessionId", 42);
            else
            {
                // These operations model SessionWatchAdapter and the per-session logoff event.
                target.StopTracking<SessionWatch>();
                session.RaiseLoggedOff();
            }
            return Task.CompletedTask;
        };
        using var context = Context(manager, watcher, instance);
        await StartContext(context);
        await context.Start(instance, TestTimeout);
        Assert.Same(session, instance.Session);
        await context.Stop(instance, TestTimeout);
        Assert.Null(instance.Session);
        Assert.Null(registry.Key.GetValue("SessionId"));
        Assert.True(manager.Queries >= 4); // Initial state, both commands, and the logoff refresh.
    }

    [Fact]
    public async Task Key_watch_follows_listener_lifetime()
    {
        using var registry = new InstanceRegistry();
        using var watch = new RegistryKeyWatch(registry.Key.OpenSubKey(string.Empty)!);

        var first = Channel.CreateUnbounded<object?>();
        var second = Channel.CreateUnbounded<object?>();
        EventHandler firstHandler = (sender, _) => first.Writer.TryWrite(sender);
        EventHandler secondHandler = (sender, _) => second.Writer.TryWrite(sender);

        watch.Changed += firstHandler;
        watch.Changed += secondHandler;
        registry.Key.SetValue("SessionId", 1);
        Assert.Same(watch, await first.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
        Assert.Same(watch, await second.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));

        watch.Changed -= firstHandler;
        registry.Key.SetValue("SessionId", 2);
        await second.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.False(first.Reader.TryRead(out _));

        watch.Changed -= secondHandler;
        registry.Key.SetValue("SessionId", 3);
        watch.Changed += firstHandler;
        registry.Key.SetValue("SessionId", 4);
        await first.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.False(second.Reader.TryRead(out _));

        watch.Dispose();
        registry.Key.SetValue("SessionId", 5);
        watch.Changed -= firstHandler; // Removing a listener after disposal remains harmless.
        Assert.False(first.Reader.TryRead(out _));
    }


    private sealed class InstanceRegistry : IDisposable
    {
        private readonly TestDuoRegistry _registry = new();
        private readonly List<SessionWatch> _watches = [];
        public RegistryKey Key { get; }
        public SessionMonitor Monitor { get; } = new(new SessionMonitorConfig(), new FakeSessionManager())
        {
            Logger = NullLogger<SessionMonitor>.Instance, Scope = null!
        };
        public InstanceRegistry()
        {
            Key = CreateKey("Player");
            Monitor.StartupFinished.Set();
        }
        public RegistryKey CreateKey(string name) => _registry.Key.CreateSubKey($@"Instances\{name}");
        public void AddSession(FakeSession session)
        {
            var watch = SessionWatch(session);
            _watches.Add(watch);
            Monitor.StartTracking(watch);
        }
        public RegistryWatcher Watcher(params FakeSession[] sessions)
        {
            foreach (var session in sessions) AddSession(session);
            return new() { SessionMonitor = Monitor, Logger = NullLogger<RegistryWatcher>.Instance };
        }
        public void Dispose()
        {
            Monitor.Dispose();
            foreach (var watch in _watches) watch.Dispose();
            Key.Dispose();
            _registry.Dispose();
        }
    }
}
