namespace MadWizard.Desomnia.Power.Source
{
    /// <summary>
    /// Base for power source probes without a native change notification:
    /// polls the source while (and only while) someone listens for changes.
    /// </summary>
    public abstract class PollingPowerSource : IPowerSource, IDisposable
    {
        static readonly TimeSpan DEFAULT_POLL_INTERVAL = TimeSpan.FromSeconds(5);

        readonly Lock _lock = new();

        EventHandler? _changed;
        Timer? _timer;

        PowerSource _last;

        protected virtual TimeSpan PollInterval => DEFAULT_POLL_INTERVAL;

        public abstract PowerSource Source { get; }

        public event EventHandler? SourceChanged
        {
            add
            {
                lock (_lock)
                {
                    _changed += value;

                    if (_timer is null && _changed is not null)
                    {
                        _last = Source;

                        _timer = new Timer(Poll, null, PollInterval, PollInterval);
                    }
                }
            }
            remove
            {
                lock (_lock)
                {
                    _changed -= value;

                    if (_changed is null && _timer is not null)
                    {
                        _timer.Dispose();
                        _timer = null;
                    }
                }
            }
        }

        /// <summary>Immediate re-check, e.g. nudged by a platform power event.</summary>
        public void Poke() => Poll(null);

        private void Poll(object? state)
        {
            EventHandler? handler = null;

            lock (_lock)
            {
                var current = Source;

                if (current != _last)
                {
                    _last = current;

                    handler = _changed;
                }
            }

            handler?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
