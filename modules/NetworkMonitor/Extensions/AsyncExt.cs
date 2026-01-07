namespace MadWizard.Desomnia.Network.Extensions
{
    internal static class SemaphoreExt
    {
        public static IDisposable Use(this SemaphoreSlim semaphore)
        {
            semaphore.Wait();

            return new SemaphoreLease(semaphore);
        }

        private class SemaphoreLease(SemaphoreSlim semaphore) : IDisposable
        {
            public void Dispose() => semaphore.Release();
        }
    }

    internal static class TaskExt
    {
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
