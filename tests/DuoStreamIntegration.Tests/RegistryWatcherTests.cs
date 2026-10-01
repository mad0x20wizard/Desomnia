using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
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
            //Assert.True((await Next(changes)).Initial);
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
        registry.Key.SetValue("SessionId", 42);
        var watcher = registry.Watcher(sessions);
        var changes = Observe(watcher);
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([instance], lifetime.Token);
        try
        {
            Assert.Null(instance.Session);
            var session = new FakeSession(42, null);
            sessions.Logon(session);
            Assert.Same(session, (await Next(changes)).Session);
        }
        finally { watcher.StopWatch(); }
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
        private readonly string _path = $@"Software\Desomnia.Tests\{Guid.NewGuid():N}";
        public RegistryKey Key { get; }
        public InstanceRegistry() => Key = CreateKey("Player");
        public RegistryKey CreateKey(string name) => Registry.CurrentUser.CreateSubKey($@"{_path}\{name}");
        public RegistryWatcher Watcher(ISessionManager sessions) => new TestRegistryWatcher(_path)
        {
            SessionManager = sessions, Logger = NullLogger.Instance
        };
        public void Dispose()
        {
            Key.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_path);
        }
    }

    private sealed class TestRegistryWatcher(string path) : RegistryWatcher
    {
        protected override RegistryKey OpenInstanceKey(DuoInstance instance) =>
            Registry.CurrentUser.OpenSubKey($@"{path}\{instance.Name}", writable: true)!;
    }
}
