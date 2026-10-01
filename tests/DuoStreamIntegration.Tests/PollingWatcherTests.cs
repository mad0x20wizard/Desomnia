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
    public async Task Startup_cancellation_propagates_and_releases_session_subscriptions()
    {
        using var instance = Instance();
        using var handler = new BlockingHandler();
        using var manager = Manager(handler);
        var logger = new RecordingLogger();
        var sessions = new FakeSessionManager();
        using var watcher = new PollingWatcher
        {
            SessionManager = sessions,
            Manager = manager,
            Logger = logger,
            PollInterval = TimeSpan.FromMilliseconds(20)
        };
        using var lifetime = new CancellationTokenSource(TestTimeout);

        var starting = watcher.StartWatch([instance], lifetime.Token);
        await handler.Entered.Task.WaitAsync(TestTimeout);

        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(TestTimeout));

        Assert.Empty(logger.Errors);
        Assert.Equal(0, sessions.LogoffSubscribers);
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
        var session = SessionFor(instance);
        var sessions = new FakeSessionManager(session);
        var manager = new ControlledManager
        {
            OnQuery = (_, _) =>
            {
                switch (Interlocked.Increment(ref queries))
                {
                    case 1: return Task.FromResult(false); // initial query
                    case 2: return Task.FromException<bool>(new TaskCanceledException("HTTP request timed out"));
                    case 3: return Task.FromResult(true);
                    default:
                        sessions.Logoff(session);
                        return Task.FromResult(false);
                }
            }
        };
        var logger = new RecordingLogger();
        var watcher = new PollingWatcher { SessionManager = sessions, Manager = manager, Logger = logger, PollInterval = TimeSpan.FromMilliseconds(20) };
        var changes = new List<bool>();
        var stopped = Signal();
        watcher.SessionChanged += (_, args) =>
        {
            changes.Add(args.Session is not null);
            if (args.Session is null) stopped.TrySetResult();
        };
        using var lifetime = new CancellationTokenSource(TestTimeout);
        await watcher.StartWatch([instance], lifetime.Token);
        try
        {
            await stopped.Task.WaitAsync(TestTimeout);
            Assert.Equal(new[] { true, false }, changes);
            Assert.Null(instance.Session);
            Assert.Single(logger.Errors);
        }
        finally
        {
            watcher.StopWatch();
        }
    }

    [Fact]
    public async Task Startup_token_does_not_stop_polling_and_StopWatch_waits_for_the_active_query()
    {
        using var instance = Instance();
        var entered = Signal();
        var canceled = Signal();
        var release = Signal();
        var calls = 0;
        var manager = new ControlledManager
        {
            OnQuery = async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1) return false;
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally
                {
                    canceled.TrySetResult();
                    await release.Task;
                }
                return false;
            }
        };
        var sessions = new FakeSessionManager();
        using var watcher = new PollingWatcher
        {
            Manager = manager, SessionManager = sessions, Logger = NullLogger.Instance,
            PollInterval = TimeSpan.FromMilliseconds(20)
        };
        using var startup = new CancellationTokenSource();
        await watcher.StartWatch([instance], startup.Token);
        await entered.Task.WaitAsync(TestTimeout);
        startup.Cancel();
        Assert.False(canceled.Task.IsCompleted);

        var stopping = Task.Run(watcher.StopWatch);
        try
        {
            await canceled.Task.WaitAsync(TestTimeout);
            Assert.False(stopping.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(TestTimeout);
        Assert.Equal(0, sessions.LogoffSubscribers);
        watcher.StopWatch();
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
