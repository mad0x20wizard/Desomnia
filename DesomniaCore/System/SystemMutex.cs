namespace MadWizard.Desomnia
{
    /**
     * On Linux donet uses a file-based mutex, which are scoped the same way as on Windows.
     * In order for the mutex to be system-wide, it must be created with a name starting with "Global\".
     */
    public class SystemMutex : IDisposable
    {
        readonly Mutex _mutex;

        public SystemMutex(string name, bool global)
        {
            _mutex = new(initiallyOwned: false, (global ? @"Global\" : "") + name, out _);

            string type = global ? "global mutex" : "mutex";

            try
            {
                if (!_mutex.WaitOne(0, exitContext: false)) // 0ms -> non-blocking: true if we acquired it (no other instance holds it)
                {
                    throw new NotSupportedException($"Another instance is already running ({type} '{name}' is held by another process).");
                }

                //Logger.LogDebug("Successfully acquired {type}: '{name}'", type, name);
            }
            catch (AbandonedMutexException)
            {
                //Logger.LogWarning("Acquired abandoned {type}: '{name}'", type, name); // Previous holder crashed; we now own it.
            }
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();

            //Logger.LogTrace("Mutex released");
        }
    }
}