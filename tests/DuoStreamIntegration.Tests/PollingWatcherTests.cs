using MadWizard.Desomnia.Service.Duo;
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
        var query = manager.QueryState(instance, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => query);
    }

    [Fact]
    public async Task Startup_timeout_is_handled_by_the_context()
    {
        using var instance = Instance();
        using var handler = new BlockingHandler();
        using var manager = Manager(handler);
        using var context = Context(manager, Polling(), instance);
        await Assert.ThrowsAsync<TimeoutException>(() => context.InitializeAsync(TimeSpan.FromMilliseconds(100)));
        Assert.True(handler.Entered.Task.IsCompleted);
        Assert.Null(instance.IsRunning);
    }

    [Fact]
    public async Task Polling_emits_refresh_signals_and_stops_on_cancellation()
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await using var reader = ((IDuoWatcher)Polling()).WatchAsync([], cancellation.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Null(reader.Current.Instance);
        Assert.Null(reader.Current.IsRunning);
        Assert.True(await reader.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task Context_recovers_from_a_poll_timeout_and_observes_start_then_stop()
    {
        using var instance = Instance();
        var calls = 0;
        var manager = new ControlledManager
        {
            OnQuery = (_, _) => Interlocked.Increment(ref calls) switch
            {
                1 => Task.FromResult(false),
                2 => Task.FromException<bool>(new TaskCanceledException("HTTP request timed out")),
                3 => Task.FromResult(true),
                _ => Task.FromResult(false)
            }
        };
        var logger = new RecordingLogger<DuoServiceContext>();
        using var context = new DuoServiceContext
        {
            Manager = manager, Watcher = Polling(), Instances = [instance], Logger = logger
        };
        var changes = new List<bool>();
        var stopped = Signal();
        instance.Started += _ => { changes.Add(true); return Task.CompletedTask; };
        instance.Stopped += _ => { changes.Add(false); stopped.TrySetResult(); return Task.CompletedTask; };
        await StartContext(context);
        await stopped.Task.WaitAsync(TestTimeout);
        context.StopWatching();

        Assert.Equal(new[] { true, false }, changes);
        Assert.False(instance.IsRunning);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task Context_stop_waits_for_the_active_query_to_finish()
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
        using var context = Context(manager, Polling(), instance);
        await StartContext(context);
        await entered.Task.WaitAsync(TestTimeout);
        var stopping = Task.Run(context.StopWatching);
        try
        {
            await canceled.Task.WaitAsync(TestTimeout);
            Assert.False(stopping.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(TestTimeout);
    }

    private static PollingWatcher Polling() => new()
    {
        Logger = NullLogger<PollingWatcher>.Instance, PollInterval = TimeSpan.FromMilliseconds(20)
    };

    private static DuoWebAPIManager Manager(HttpMessageHandler handler) => new(new HttpClient(handler)
    {
        BaseAddress = new Uri("http://localhost")
    })
    {
        Logger = NullLogger<DuoWebAPIManager>.Instance
    };

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = Signal();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The request unexpectedly resumed without cancellation.");
        }
    }
}
