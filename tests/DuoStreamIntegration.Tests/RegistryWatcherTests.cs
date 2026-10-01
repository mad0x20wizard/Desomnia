using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
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
    public async Task Adding_and_removing_the_value_controls_the_association(string? clientName)
    {
        using var instance = Instance();
        var session = new FakeSession(42, clientName);
        var sessions = new FakeSessionManager(session);
        using var registry = new InstanceRegistry();
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([instance], lifetime.Token);
        try
        {
            Assert.Null(instance.Session);
            registry.Key.SetValue("SessionId", 42);
            Assert.Same(session, (await Next(changes)).Session);

            registry.Key.SetValue("DisplayName", "Changed name");
            registry.Key.SetValue("SessionId", 42);
            registry.Key.DeleteValue("SessionId");
            Assert.Null((await Next(changes)).Session);
            Assert.Null(instance.Session);
        }
        finally { watcher.StopWatch(); }
        Assert.Equal(0, sessions.LogoffSubscribers);
    }

    [Fact]
    public async Task Logoff_removes_the_retained_value_and_publishes_one_stop()
    {
        using var instance = Instance();
        var session = new FakeSession(42, null);
        var sessions = new FakeSessionManager(session);
        using var registry = new InstanceRegistry();
        registry.Key.SetValue("SessionId", 42);
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([instance], lifetime.Token);
        try
        {
            Assert.Same(session, (await Next(changes)).Session);
            sessions.Logoff(session);
            Assert.Null((await Next(changes)).Session);
            Assert.Null(registry.Key.GetValue("SessionId"));

            // An unrelated logon must not revive the old association through a retained ID.
            var replacement = new FakeSession(43, null);
            sessions.Logon(replacement);
            registry.Key.SetValue("SessionId", 43);
            Assert.Same(replacement, (await Next(changes)).Session);
        }
        finally { watcher.StopWatch(); }
    }

    [Fact]
    public async Task Value_written_before_logon_is_resolved_when_the_session_arrives()
    {
        using var instance = Instance();
        var sessions = new FakeSessionManager();
        using var registry = new InstanceRegistry();
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([instance], lifetime.Token);
        try
        {
            var requested = Signal();
            sessions.SessionRequested = _ => requested.TrySetResult();
            registry.Key.SetValue("SessionId", 42);
            await requested.Task.WaitAsync(TestTimeout);
            Assert.Equal(42, registry.Key.GetValue("SessionId"));
            Assert.Null(instance.Session);
            var session = new FakeSession(42, null);
            sessions.Logon(session);
            Assert.Same(session, (await Next(changes)).Session);
        }
        finally { watcher.StopWatch(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("another-user")]
    public async Task Startup_clears_stale_ids_and_preserves_matching_sessions(string? staleUser)
    {
        using var stale = Instance();
        using var active = Instance("Active");
        var validSession = new FakeSession(43, null, "PLAYER");
        var sessions = new FakeSessionManager(validSession);
        if (staleUser is not null) sessions.Logon(new FakeSession(42, null, staleUser));
        using var registry = new InstanceRegistry();
        using var activeKey = registry.CreateKey("Active");
        registry.Key.SetValue("SessionId", 42);
        activeKey.SetValue("SessionId", 43);
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);

        await watcher.StartWatch([stale, active], CancellationToken.None);
        try
        {
            Assert.Null(registry.Key.GetValue("SessionId"));
            Assert.Equal(43, activeKey.GetValue("SessionId"));
            var started = await Next(changes);
            Assert.Same(active, started.Instance);
            Assert.Same(validSession, started.Session);

            // Reusing the stale ID must not revive the old association.
            var reused = new FakeSession(42, null);
            sessions.Logon(reused);
            activeKey.DeleteValue("SessionId");
            Assert.Same(active, (await Next(changes)).Instance);
            Assert.False(changes.Reader.TryRead(out _));
        }
        finally { watcher.StopWatch(); }
    }

    [Fact]
    public async Task Two_instances_with_the_same_user_cannot_claim_the_same_session()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var session = new FakeSession(42, null);
        var sessions = new FakeSessionManager(session);
        using var registry = new InstanceRegistry();
        using var alphaKey = registry.CreateKey("Alpha");
        using var betaKey = registry.CreateKey("Beta");
        alphaKey.SetValue("SessionId", 42);
        betaKey.SetValue("SessionId", 42);
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);

        await watcher.StartWatch([alpha, beta], CancellationToken.None);
        try
        {
            Assert.Same(alpha, (await Next(changes)).Instance);
            Assert.Equal(42, alphaKey.GetValue("SessionId"));
            Assert.Null(betaKey.GetValue("SessionId"));
            sessions.Logoff(session);
            var stopped = await Next(changes);
            Assert.Same(alpha, stopped.Instance);
            Assert.Null(stopped.Session);
            Assert.False(changes.Reader.TryRead(out _));
        }
        finally { watcher.StopWatch(); }
    }

    [Fact]
    public async Task Automated_start_rejects_a_stale_id_owned_by_a_different_user()
    {
        using var marc = new DuoInstance("Marc", Settings("Marc", "Marc"), new DuoInstanceWatchInfo { Name = "Marc" });
        using var target = new DuoInstance("Test", Settings("Test", "Kevin"), new DuoInstanceWatchInfo { Name = "Test" });
        var session = new FakeSession(31, null, "Kevin");
        var sessions = new FakeSessionManager();
        var config = new SessionMonitorConfig();
        using var monitor = new SessionMonitor(config, sessions)
        {
            Logger = NullLogger<SessionMonitor>.Instance, Scope = null!
        };
        monitor.StartupFinished.Set();
        using var sessionWatch = SessionWatch(session);
        monitor.StartTracking(sessionWatch);
        using var registry = new InstanceRegistry();
        using var marcKey = registry.CreateKey("Marc");
        using var targetKey = registry.CreateKey("Test");
        var logger = new RecordingLogger();
        using var watcher = new RegistryWatcher { SessionManager = sessions, Logger = logger };
        var changes = Observe(watcher);
        var manager = new ControlledManager
        {
            OnChange = (_, _, _) =>
            {
                sessions.Logon(session);
                targetKey.SetValue("SessionId", 31);
                return Task.CompletedTask;
            }
        };
        using var context = new DuoServiceContext
        {
            Settings = new() { Port = 38299, Instances = [marc.Settings, target.Settings] },
            Manager = manager, Watcher = watcher, Instances = [marc, target],
            SessionMonitor = monitor, SessionMonitorConfig = config
        };
        await context.StartWatching(TestTimeout);

        // A stale value appears before Windows announces Test's new session.
        var requested = Signal();
        sessions.SessionRequested = _ => requested.TrySetResult();
        marcKey.SetValue("SessionId", 31);
        await requested.Task.WaitAsync(TestTimeout);
        await context.Start(target, TestTimeout);

        Assert.Null(marc.Session);
        Assert.Null(marcKey.GetValue("SessionId"));
        Assert.Same(session, target.Session);
        Assert.True(sessionWatch.Watch.IsYield);
        Assert.Same(target, (await Next(changes)).Instance);
        Assert.False(changes.Reader.TryRead(out _));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Failed_notification_can_be_retried_after_the_context_has_attached_the_session()
    {
        using var instance = Instance();
        var session = new FakeSession(42, null);
        var sessions = new FakeSessionManager(session);
        var config = new SessionMonitorConfig();
        using var monitor = new SessionMonitor(config, sessions)
        {
            Logger = NullLogger<SessionMonitor>.Instance, Scope = null!
        };
        monitor.StartupFinished.Set();
        using var sessionWatch = SessionWatch(session);
        monitor.StartTracking(sessionWatch);
        using var registry = new InstanceRegistry();
        var logger = new RecordingLogger();
        using var watcher = new RegistryWatcher { SessionManager = sessions, Logger = logger };
        using var context = new DuoServiceContext
        {
            Settings = new() { Port = 38299, Instances = [instance.Settings] },
            Manager = new ControlledManager(), Watcher = watcher, Instances = [instance],
            SessionMonitor = monitor, SessionMonitorConfig = config
        };
        await context.StartWatching(TestTimeout);
        var delivered = Signal();
        var attempts = 0;
        watcher.SessionChanged += (_, _) =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Subscriber failed");
            delivered.TrySetResult();
        };

        registry.Key.SetValue("SessionId", 42);
        await logger.ErrorReported.Task.WaitAsync(TestTimeout);
        Assert.Same(session, instance.Session);
        registry.Key.SetValue("DisplayName", "Retry");
        await delivered.Task.WaitAsync(TestTimeout);

        Assert.Equal(2, attempts);
        Assert.Same(sessionWatch, Assert.Single(instance.OfType<SessionWatch>()));
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task Logoff_of_the_previous_session_does_not_delete_a_replacement_id()
    {
        using var instance = Instance();
        var first = new FakeSession(42, null);
        var second = new FakeSession(43, null);
        var sessions = new FakeSessionManager(first, second);
        using var registry = new InstanceRegistry();
        registry.Key.SetValue("SessionId", 42);
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([instance], lifetime.Token);
        try
        {
            await Next(changes);
            registry.Key.SetValue("SessionId", 43);
            sessions.Logoff(first);
            while ((await Next(changes)).Session != second) { }
            Assert.Equal(43, registry.Key.GetValue("SessionId"));
        }
        finally { watcher.StopWatch(); }
    }

    [Fact]
    public async Task Context_commands_complete_from_registry_changes_without_querying_the_API()
    {
        using var instance = Instance();
        var session = new FakeSession(42, null);
        var sessions = new FakeSessionManager(session);
        using var registry = new InstanceRegistry();
        var watcher = registry.Watcher(sessions);
        var manager = new ControlledManager
        {
            OnQuery = (_, _) => throw new InvalidOperationException("Registry watching must not query the API"),
            OnChange = (_, running, _) =>
            {
                if (running) registry.Key.SetValue("SessionId", 42);
                else sessions.Logoff(session);
                return Task.CompletedTask;
            }
        };
        using var context = Context(manager, watcher, instance);
        await context.StartWatching(TestTimeout);
        await context.Start(instance, TestTimeout);
        Assert.Same(session, instance.Session);
        await context.Stop(instance, TestTimeout);
        Assert.Null(instance.Session);
        Assert.Null(registry.Key.GetValue("SessionId"));
        Assert.Equal(0, manager.Queries);
    }

    [Fact]
    public async Task Logoff_only_clears_the_associated_instance()
    {
        using var alpha = Instance("Alpha");
        using var beta = Instance("Beta");
        var first = new FakeSession(42, null);
        var second = new FakeSession(43, null);
        var sessions = new FakeSessionManager(first, second);
        using var registry = new InstanceRegistry();
        using var alphaKey = registry.CreateKey("Alpha");
        using var betaKey = registry.CreateKey("Beta");
        alphaKey.SetValue("SessionId", 42);
        betaKey.SetValue("SessionId", 43);
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([alpha, beta], lifetime.Token);
        try
        {
            Assert.Same(alpha, (await Next(changes)).Instance);
            Assert.Same(beta, (await Next(changes)).Instance);
            sessions.Logoff(first);
            var stopped = await Next(changes);
            Assert.Same(alpha, stopped.Instance);
            Assert.Null(stopped.Session);
            Assert.Null(alphaKey.GetValue("SessionId"));

            Assert.Equal(43, betaKey.GetValue("SessionId"));
        }
        finally { watcher.StopWatch(); }
    }

    [Fact]
    public async Task Key_watch_follows_listener_lifetime()
    {
        using var registry = new InstanceRegistry();
        using var watch = new KeyWatch(registry.Key.OpenSubKey(string.Empty)!);

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

    private static Channel<InstanceSessionChangedEventArgs> Observe(RegistryWatcher watcher)
    {
        var changes = Channel.CreateUnbounded<InstanceSessionChangedEventArgs>();
        watcher.SessionChanged += (_, args) => changes.Writer.TryWrite(args);
        return changes;
    }

    private static async Task<InstanceSessionChangedEventArgs> Next(Channel<InstanceSessionChangedEventArgs> changes) =>
        await changes.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

    private sealed class InstanceRegistry : IDisposable
    {
        private readonly TestDuoRegistry _registry = new();
        public RegistryKey Key { get; }
        public InstanceRegistry() => Key = CreateKey("Player");
        public RegistryKey CreateKey(string name) => _registry.Key.CreateSubKey($@"Instances\{name}");
        public RegistryWatcher Watcher(ISessionManager sessions) => new()
        {
            SessionManager = sessions, Logger = NullLogger.Instance
        };
        public void Dispose()
        {
            Key.Dispose();
            _registry.Dispose();
        }
    }
}
