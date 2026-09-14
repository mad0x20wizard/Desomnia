using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DuoStreamIntegration.Tests.DuoTestSupport;

namespace DuoStreamIntegration.Tests;

public sealed class PollingWatcherTests
{
    [Fact]
    public async Task Refit_preserves_the_original_task_cancellation_exception()
    {
        using var instance = Instance();
        using var handler = new BlockingHandler();
        using var manager = Manager(handler);
        using var cancellation = new CancellationTokenSource(TestTimeout);

        var query = manager.QueryRunningState(instance, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TestTimeout);

        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => query);
    }

    [Fact]
    public async Task Cancellation_of_an_active_refit_query_stops_polling_normally()
    {
        using var instance = Instance();
        using var handler = new BlockingHandler();
        using var manager = Manager(handler);
        var logger = new RecordingLogger();
        var watcher = new PollingWatcher
        {
            Manager = manager,
            Logger = logger,
            PollInterval = TimeSpan.FromMilliseconds(20)
        };
        using var lifetime = new CancellationTokenSource(TestTimeout);

        var watching = watcher.WatchAsync([instance], lifetime.Token);
        await handler.Entered.Task.WaitAsync(TestTimeout);

        lifetime.Cancel();
        await watching.WaitAsync(TestTimeout);

        Assert.Empty(logger.Errors);
    }

    private static DuoWebAPIManager Manager(HttpMessageHandler handler) => new(new HttpClient(handler)
    {
        BaseAddress = new Uri("http://localhost")
    })
    {
        Logger = NullLogger<DuoWebAPIManager>.Instance
    };

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

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = Signal();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("The request unexpectedly resumed without cancellation.");
        }
    }
}
