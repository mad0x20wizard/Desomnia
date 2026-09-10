using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    // Owns one service run. Cancellation closes admission immediately; disposal waits
    // for cancellation callbacks and borrowed operations before releasing resources.
    internal sealed class DuoGeneration : IAsyncDisposable
    {
        private readonly object _mutex = new();
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource _canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IDisposable? _lifetimeRegistration;
        private int _operationCount;
        private bool _cancelStarted;
        private int _disposed;

        public DuoGeneration(
            uint processId,
            DuoApiConnection connection,
            DuoInstance[] instances,
            CancellationToken lifetimeToken,
            IDisposable? lifetimeRegistration,
            ILogger logger)
        {
            _logger = logger;
            ProcessId = processId;
            Connection = connection;
            Instances = instances;
            _lifetimeRegistration = lifetimeRegistration;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            Token = _cancellation.Token;
        }

        public uint ProcessId { get; }
        public DuoApiConnection Connection { get; }
        public IDuoWebManager API => Connection.API;
        public DuoInstance[] Instances { get; }
        public CancellationToken Token { get; }
        public bool IsInvalidated => Volatile.Read(ref _cancelStarted) || Token.IsCancellationRequested;

        public IDisposable? TryBeginOperation()
        {
            lock (_mutex)
            {
                if (IsInvalidated)
                    return null;

                _operationCount++;
                return new Operation(this);
            }
        }

        public Task CancelAsync()
        {
            lock (_mutex)
            {
                if (_cancelStarted)
                    return _canceled.Task;

                _cancelStarted = true;
                CompleteDrain();
            }

            // User cancellation callbacks and process unsubscription run outside the lock.
            try
            {
                _cancellation.Cancel();
            }
            catch (Exception ex)
            {
                LogCleanupError(_logger, ex, "Could not cancel Duo generation {pid}", ProcessId);
            }

            if (_lifetimeRegistration != null)
                DisposeResources(_logger, [_lifetimeRegistration]);

            _canceled.TrySetResult();
            return _canceled.Task;
        }

        private void EndOperation()
        {
            lock (_mutex)
            {
                _operationCount--;
                CompleteDrain();
            }
        }

        private void CompleteDrain()
        {
            if (_cancelStarted && _operationCount == 0)
                _drained.TrySetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await CancelAsync();
            await _drained.Task;

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            DisposeResources(_logger, [.. Instances, Connection, _cancellation]);
        }

        internal static void DisposeResources(ILogger logger, IEnumerable<IDisposable> resources)
        {
            foreach (var resource in resources)
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception ex)
                {
                    LogCleanupError(logger, ex, "Could not dispose Duo resource {resource}", resource);
                }
            }
        }

        internal static void LogCleanupError(ILogger logger, Exception error, string message, params object?[] args)
        {
            try
            {
                logger.LogError(error, message, args);
            }
            catch
            {
                // Container teardown may have already disposed the logging pipeline.
            }
        }

        private sealed class Operation(DuoGeneration owner) : IDisposable
        {
            private DuoGeneration? _owner = owner;

            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndOperation();
        }
    }
}
