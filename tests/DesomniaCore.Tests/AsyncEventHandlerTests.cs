using Xunit;

namespace MadWizard.Desomnia.Tests;

public class AsyncEventHandlerTests
{
    [Fact]
    public async Task NoSubscribersCompletesSuccessfully()
    {
        AsyncEventHandler? handlers = null;
        await handlers.InvokeAsync(this, EventArgs.Empty);
    }

    [Fact]
    public async Task SubscribersAreAwaitedInOrderWithOriginalArguments()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<int>();
        var args = new EventArgs();
        AsyncEventHandler handlers = async (sender, receivedArgs) =>
        {
            Assert.Same(this, sender);
            Assert.Same(args, receivedArgs);
            calls.Add(1);
            await release.Task;
            calls.Add(2);
        };
        handlers += (_, _) => { calls.Add(3); return Task.CompletedTask; };
        var invocation = handlers.InvokeAsync(this, args);
        try
        {
            Assert.Equal(new[] { 1 }, calls);
            Assert.False(invocation.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await invocation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public async Task FailuresAndCancellationAreAggregatedWithoutSkippingSubscribers()
    {
        var syncFailure = new InvalidOperationException("Synchronous failure");
        var asyncFailure = new ArgumentException("Asynchronous failure");
        var canceled = new CancellationToken(true);
        bool lastCalled = false;
        AsyncEventHandler handlers = (_, _) => throw syncFailure;
        handlers += async (_, _) => { await Task.Yield(); throw asyncFailure; };
        handlers += (_, _) => Task.FromCanceled(canceled);
        handlers += (_, _) => { lastCalled = true; return Task.CompletedTask; };

        var invocation = handlers.InvokeAsync(this, EventArgs.Empty);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => invocation);
        Assert.True(lastCalled);
        Assert.True(invocation.IsFaulted);
        Assert.Collection(failure.InnerExceptions,
            ex => Assert.Same(syncFailure, ex),
            ex => Assert.Same(asyncFailure, ex),
            ex => Assert.Equal(canceled, Assert.IsAssignableFrom<OperationCanceledException>(ex).CancellationToken));
    }
}
