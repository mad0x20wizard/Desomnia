namespace System.Threading
{
    public delegate Task AsyncEventHandler(object? sender, EventArgs args);

    public static class AsyncEventHandlerExt
    {
        /// <summary>
        /// Awaits each subscriber in order, preserving the synchronization context.
        /// All subscribers run even if some fail; failures are collected in an AggregateException.
        /// </summary>
        public static async Task InvokeAsync(this AsyncEventHandler? handlers, object? sender, EventArgs args)
        {
            if (handlers is object)
            {
                Delegate[]? individualHandlers = handlers.GetInvocationList();
                List<Exception>? exceptions = null;
                foreach (AsyncEventHandler handler in individualHandlers)
                {
                    try
                    {
                        await handler(sender, args).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        if (exceptions is null)
                        {
                            exceptions = new List<Exception>(2);
                        }

                        exceptions.Add(ex);
                    }
                }

                if (exceptions is object)
                {
                    throw new AggregateException(exceptions);
                }
            }
        }
    }

    public static class SemaphoreExt
    {
        public static int ReleaseFinally(this SemaphoreSlim semaphore)
        {
            try
            {
                return semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                return 0; // ignore if semaphore is already disposed
            }
        }
    }

    namespace Channels
    {
        public class LocalChannel<T>(Channel<T> channel) : IDisposable
        {
            public ChannelReader<T> Reader => channel.Reader;
            public ChannelWriter<T> Writer => channel.Writer;

            void IDisposable.Dispose()
            {
                channel.Writer.TryComplete();
            }

            public static implicit operator LocalChannel<T>(Channel<T> channel) => new(channel);
        }
    }

    namespace Tasks
    {
        public static class TaskExt
        {
            extension (Task task)
            {
                public void ThrowIfFaulted()
                {
                    if (task.IsCompleted)
                    {
                        task.GetAwaiter().GetResult();
                    }
                }

                public static async Task<bool> DelayIfNotCancelled(TimeSpan delay, CancellationToken cancellationToken)
                {
                    try
                    {
                        await Task.Delay(delay, cancellationToken);

                        return true;
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                }
            }
        }
    }
}
