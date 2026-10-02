using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Service.Duo;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Xunit;

namespace DuoStreamIntegration.Tests;

public class CompositeWatcherTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Signals_from_all_watchers_arrive_without_waiting_for_an_idle_watcher()
    {
        var first = new ChannelWatcher();
        var second = new ChannelWatcher();
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var reader = Create(first, second).WatchAsync([], cancellation.Token).GetAsyncEnumerator();

        var next = reader.MoveNextAsync().AsTask();
        await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(cancellation.Token);

        second.Signals.Writer.TryWrite(new(running: true));
        Assert.True(await next.WaitAsync(cancellation.Token));
        Assert.Equal(true, reader.Current.IsRunning);

        first.Signals.Writer.TryWrite(new(running: false));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(false, reader.Current.IsRunning);

        second.Signals.Writer.TryWrite(new());
        second.Signals.Writer.TryWrite(new());
        second.Signals.Writer.TryComplete();
        first.Signals.Writer.TryComplete();

        // Query signals and duplicates must reach the context unchanged as well.
        Assert.True(await reader.MoveNextAsync());
        Assert.Null(reader.Current.IsRunning);
        Assert.True(await reader.MoveNextAsync());
        Assert.Null(reader.Current.IsRunning);
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task Completing_one_watcher_does_not_complete_the_timeline()
    {
        var first = new ChannelWatcher();
        var second = new ChannelWatcher();
        first.Signals.Writer.TryComplete();
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var reader = Create(first, second).WatchAsync([], cancellation.Token).GetAsyncEnumerator();

        var next = reader.MoveNextAsync().AsTask();
        await first.Finished.Task.WaitAsync(cancellation.Token);
        Assert.False(next.IsCompleted);

        second.Signals.Writer.TryWrite(new(running: true));
        second.Signals.Writer.TryComplete();
        Assert.True(await next.WaitAsync(cancellation.Token));
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task Cancellation_stops_and_awaits_all_watchers()
    {
        var first = new ChannelWatcher();
        var second = new ChannelWatcher();
        using var cancellation = new CancellationTokenSource();
        await using var reader = Create(first, second).WatchAsync([], cancellation.Token).GetAsyncEnumerator();

        var next = reader.MoveNextAsync().AsTask();
        await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(Timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(Timeout));
        Assert.True(first.Finished.Task.IsCompletedSuccessfully);
        Assert.True(second.Finished.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Disposing_the_reader_stops_all_watchers_without_cancelling_the_caller()
    {
        var first = new ChannelWatcher();
        var second = new ChannelWatcher();
        first.Signals.Writer.TryWrite(new());
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var reader = Create(first, second).WatchAsync([], cancellation.Token).GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        await reader.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.False(cancellation.IsCancellationRequested);
        Assert.True(first.Finished.Task.IsCompletedSuccessfully);
        Assert.True(second.Finished.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_failed_watcher_does_not_stop_the_other_watchers()
    {
        var first = new ChannelWatcher();
        var second = new ChannelWatcher();
        first.Signals.Writer.TryComplete(new InvalidOperationException("Watcher failed"));
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var reader = Create(first, second).WatchAsync([], cancellation.Token).GetAsyncEnumerator();

        var next = reader.MoveNextAsync().AsTask();
        await first.Finished.Task.WaitAsync(cancellation.Token);
        second.Signals.Writer.TryWrite(new(running: false));
        second.Signals.Writer.TryComplete();

        Assert.True(await next.WaitAsync(cancellation.Token));
        Assert.Equal(false, reader.Current.IsRunning);
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task An_empty_composite_completes()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var reader = Create().WatchAsync([], cancellation.Token).GetAsyncEnumerator();

        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task Plugin_registration_resolves_a_composite_for_each_context()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(NullLogger<CompositeWatcher>.Instance).As<ILogger<CompositeWatcher>>();
        builder.RegisterInstance(NullLogger<PollingWatcher>.Instance).As<ILogger<PollingWatcher>>();

        typeof(PluginModule).GetMethod("RegisterWatchers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new PluginModule(), [builder, new DuoSessionMonitorConfig
            {
                ServiceName = "TestDuo",
                WatchMode = WatchMode.Polling,
                PollInterval = TimeSpan.FromSeconds(1)
            }]);

        using var container = builder.Build();
        using var first = container.BeginLifetimeScope(new TypedService(typeof(DuoServiceContext)));
        using var second = container.BeginLifetimeScope(new TypedService(typeof(DuoServiceContext)));

        var watcher = first.Resolve<IDuoWatcher>();
        Assert.IsType<CompositeWatcher>(watcher);
        Assert.Same(watcher, first.Resolve<IDuoWatcher>());
        Assert.NotSame(watcher, second.Resolve<IDuoWatcher>());
        Assert.Collection(first.Resolve<IEnumerable<IDuoWatcher>>(),
            item => Assert.IsType<SessionWatcher>(item),
            item => Assert.IsType<PollingWatcher>(item));

        using var cancellation = new CancellationTokenSource(Timeout);
        await using var reader = watcher.WatchAsync([], cancellation.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Null(reader.Current.Instance);
        Assert.Null(reader.Current.IsRunning);
    }

    private static IDuoWatcher Create(params IDuoWatcher[] watchers) => new CompositeWatcher(watchers)
    {
        Logger = NullLogger<CompositeWatcher>.Instance
    };

    private class ChannelWatcher : IDuoWatcher
    {
        internal Channel<WatchSignal> Signals { get; } = Channel.CreateUnbounded<WatchSignal>();
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<WatchSignal> WatchAsync(IEnumerable<DuoInstance> instances, [EnumeratorCancellation] CancellationToken token)
        {
            Started.TrySetResult();
            try
            {
                await foreach (var signal in Signals.Reader.ReadAllAsync(token))
                {
                    yield return signal;
                }
            }
            finally
            {
                Finished.TrySetResult();
            }
        }
    }
}
