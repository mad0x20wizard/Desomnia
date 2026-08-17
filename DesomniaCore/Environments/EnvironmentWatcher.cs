using NLog;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Observes one configuration generation's resolved environment conditions. Change
    /// events are debounced; when the debounce elapses the monitor re-evaluates and, if the
    /// effective configuration changed, publishes it and signals the reload.
    /// </summary>
    internal sealed class EnvironmentWatcher : IDisposable
    {
        static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        readonly EnvironmentMonitor _monitor;
        readonly IReadOnlyList<IEnvironmentCondition> _conditions;
        readonly TimeSpan _debounce;

        readonly Timer _timer;
        readonly Lock _lock = new();

        bool _disposed;

        public EnvironmentWatcher(EnvironmentMonitor monitor, IReadOnlyList<IEnvironmentCondition> conditions, TimeSpan debounce)
        {
            _monitor = monitor;
            _conditions = conditions;
            _debounce = debounce;

            _timer = new Timer(Reevaluate);

            try
            {
                foreach (var condition in _conditions)
                    condition.Changed += OnConditionChanged;
            }
            catch
            {
                // a faulting subscription must not leave half-wired conditions or a live timer behind
                Dispose();
                throw;
            }

            // catch anything that changed between the initial evaluation and this point
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }

        private void OnConditionChanged(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _timer.Change(_debounce, Timeout.InfiniteTimeSpan); // (re)arm one-shot
            }
        }

        private void Reevaluate(object? state)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
            }

            // outside the lock, so Reload (which disposes this watcher while holding the
            // monitor's lock) can never deadlock against this callback taking that lock
            try
            {
                _monitor.Reevaluate();
            }
            catch (Exception ex)
            {
                // e.g. onConflict="error" in the re-merged result; a timer callback must never throw
                Logger.Error(ex, "Failed to re-evaluate the environment configuration - keeping the current one.");
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
            }

            foreach (var condition in _conditions)
                condition.Changed -= OnConditionChanged;

            _timer.Dispose();
        }
    }
}
