using MadWizard.Desomnia.Processes.Manager;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal sealed class DuoProcessSubscription : IDisposable
    {
        private readonly object _mutex = new();
        private readonly EventHandler _stopped;
        private readonly CancellationTokenSource _lifetime;
        private IProcess? _process;
        private bool _disposed;

        public DuoProcessSubscription(IProcess process, CancellationToken stoppingToken, Action signal)
        {
            _process = process;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Token = _lifetime.Token;

            _stopped = (_, _) =>
            {
                lock (_mutex)
                {
                    if (!_disposed)
                        CancelLifetime();
                }

                signal();
            };

            process.Stopped += _stopped;
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            var process = Interlocked.Exchange(ref _process, null);

            if (process == null)
                return;

            try
            {
                process.Stopped -= _stopped;
            }
            finally
            {
                lock (_mutex)
                {
                    if (!_disposed)
                    {
                        _disposed = true;
                        CancelLifetime();
                        _lifetime.Dispose();
                    }
                }
            }
        }

        private void CancelLifetime()
        {
            try
            {
                _lifetime.Cancel();
            }
            catch
            {
                // The reconciliation signal must still be delivered.
            }
        }
    }
}
