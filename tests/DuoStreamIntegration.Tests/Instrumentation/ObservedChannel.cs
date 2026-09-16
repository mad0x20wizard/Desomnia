using System.Threading.Channels;

namespace DuoStreamIntegration.Tests;

internal interface IObservedChannel
{
    Task LastWrite { get; }
    long Writes { get; }
    Task Write(object value);
    void Finish();
}

// Acknowledge an item when the real consumer asks for its next item, after its
// processing has finished. This avoids sleeps and assertions about thread timing.
internal sealed class ObservedChannel<T> : Channel<T>, IObservedChannel
{
    private sealed record Item(T Value, TaskCompletionSource Processed);
    private readonly Channel<Item> _items = Channel.CreateUnbounded<Item>(new() { SingleReader = true });
    private TaskCompletionSource? _processing;
    private long _writes;
    public Task LastWrite { get; private set; } = Task.CompletedTask;
    public long Writes => Volatile.Read(ref _writes);

    public ObservedChannel()
    {
        Reader = new ObservedReader(this);
        Writer = new ObservedWriter(this);
    }

    public Task Write(object value)
    {
        if (!Writer.TryWrite((T)value)) throw new InvalidOperationException("Watcher has stopped.");
        return LastWrite;
    }

    public void Finish() => Interlocked.Exchange(ref _processing, null)?.TrySetResult();

    private sealed class ObservedReader(ObservedChannel<T> owner) : ChannelReader<T>
    {
        public override Task Completion => owner._items.Reader.Completion;
        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            owner._items.Reader.WaitToReadAsync(cancellationToken);
        public override bool TryRead(out T item)
        {
            owner.Finish();
            if (owner._items.Reader.TryRead(out var next))
            {
                owner._processing = next.Processed;
                item = next.Value;
                return true;
            }
            item = default!;
            return false;
        }
    }

    private sealed class ObservedWriter(ObservedChannel<T> owner) : ChannelWriter<T>
    {
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            owner._items.Writer.WaitToWriteAsync(cancellationToken);
        public override bool TryComplete(Exception? error = null)
        {
            owner.Finish();
            return owner._items.Writer.TryComplete(error);
        }
        public override bool TryWrite(T item)
        {
            var processed = DuoTestSupport.Signal();
            owner.LastWrite = processed.Task;
            Interlocked.Increment(ref owner._writes);
            return owner._items.Writer.TryWrite(new(item, processed));
        }
    }
}
