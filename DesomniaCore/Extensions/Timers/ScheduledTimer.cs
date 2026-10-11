namespace System.Timers
{
    /// <summary>
    /// A one-shot timer that fires at an absolute <see cref="DueTime"/>, not bound to the
    /// ~24.9 day (<see cref="int.MaxValue"/> ms) ceiling of <see cref="System.Timers.Timer"/>.
    /// The target may be changed at any time, including while running.
    /// </summary>
    /// <remarks>
    /// A single <see cref="System.Threading.Timer"/> is re-armed in chunks of at most
    /// <see cref="int.MaxValue"/> ms until the target instant is reached, so no thread is
    /// consumed while idle. <see cref="Elapsed"/> is raised on a thread-pool thread; exceptions
    /// thrown by handlers are swallowed, mirroring <see cref="System.Timers.Timer"/>.
    /// </remarks>
    public sealed class ScheduledTimer : IDisposable
    {
        private static readonly TimeSpan MaxChunk = TimeSpan.FromMilliseconds(int.MaxValue);

        private readonly object _gate = new();
        private readonly System.Threading.Timer _timer;

        private DateTime? _dueTime;
        private bool _enabled;
        private bool _disposed;

        public ScheduledTimer()
        {
            _timer = new(OnTick);
        }

        public ScheduledTimer(DateTime dueTime) : this()
        {
            _dueTime = dueTime;
        }

        /// <summary>Raised once when <see cref="DueTime"/> is reached.</summary>
        public event ElapsedEventHandler? Elapsed;

        /// <summary>
        /// The absolute point in time to fire at. May be changed at any time, including while
        /// running; while running, changing it re-arms the timer. Set to <c>null</c> to clear
        /// the target (the timer will not fire until a new value is set and it is started).
        /// </summary>
        public DateTime? DueTime
        {
            get { lock (_gate) { return _dueTime; } }
            set
            {
                lock (_gate)
                {
                    ThrowIfDisposed();

                    _dueTime = value;

                    if (_enabled)
                        Arm();
                }
            }
        }

        /// <summary>Whether the timer is running. Equivalent to <see cref="Start"/>/<see cref="Stop"/>.</summary>
        public bool Enabled
        {
            get { lock (_gate) { return _enabled; } }
            set { if (value) Start(); else Stop(); }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (_dueTime is null)
                    throw new InvalidOperationException("Set DueTime before starting the timer.");

                _enabled = true;
                Arm();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _enabled = false;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _enabled = false;
                _timer.Dispose();
            }
        }

        private void Arm()
        {
            // Caller holds _gate.
            if (_dueTime is null)
            {
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            var remaining = _dueTime.Value - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var chunk = remaining > MaxChunk ? MaxChunk : remaining;
            _timer.Change(chunk, Timeout.InfiniteTimeSpan);
        }

        private void OnTick(object? state)
        {
            DateTime signalTime;

            lock (_gate)
            {
                if (_disposed || !_enabled || _dueTime is null)
                    return;

                var now = DateTime.Now;
                if (now < _dueTime.Value)
                {
                    // Woke up because the wait was chunked, not because the target was reached.
                    Arm();
                    return;
                }

                signalTime = now;
                _enabled = false; // one-shot
            }

            try
            {
                Elapsed?.Invoke(this, new ElapsedEventArgs(signalTime));
            }
            catch
            {
                // Swallow to mirror System.Timers.Timer and to protect the thread-pool thread.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScheduledTimer));
        }
    }
}
