using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class PollingWatcherTests
{
    [Fact]
    public async Task Polling_recovers_from_a_request_timeout_and_observes_start_then_stop()
    {
        using var instance = Instance();
        var queries = 0;
        var manager = new ControlledManager
        {
            OnQuery = (_, _) => Interlocked.Increment(ref queries) switch
            {
                1 => Task.FromException<bool>(new TaskCanceledException("HTTP request timed out")),
                2 => Task.FromResult(true),
                _ => Task.FromResult(false)
            }
        };
        var logger = new RecordingLogger();
        var watcher = new PollingWatcher { Manager = manager, Logger = logger, PollInterval = TimeSpan.FromMilliseconds(20) };
        var changes = new List<bool>();
        var stopped = Signal();
        watcher.StatusChanged += (_, args) =>
        {
            changes.Add(args.Status);
            if (!args.Status) stopped.TrySetResult();
        };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        var watching = watcher.WatchAsync([instance], lifetime.Token);
        try
        {
            await stopped.Task.WaitAsync(TestTimeout);
            Assert.Equal(new[] { true, false }, changes);
            Assert.False(instance.IsRunning);
            Assert.Single(logger.Errors);
        }
        finally
        {
            lifetime.Cancel();
            await watching.WaitAsync(TestTimeout);
        }
    }
}
