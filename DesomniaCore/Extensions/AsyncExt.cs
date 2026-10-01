namespace System.Threading
{
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
