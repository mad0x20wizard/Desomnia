namespace MadWizard.Desomnia.Service.Duo.Manager
{
    internal sealed class DuoGeneration : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly TaskCompletionSource<bool> _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IDisposable? _lifetimeRegistration;
        private int _operationCount;
        private int _invalidated;
        private int _disposed;

        public DuoGeneration(
            uint processId,
            DuoApiConnection connection,
            DuoInstance[] instances,
            CancellationToken lifetimeToken,
            IDisposable? lifetimeRegistration)
        {
            ProcessId = processId;
            Connection = connection;
            Instances = instances;
            _lifetimeRegistration = lifetimeRegistration;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            Token = _cancellation.Token;
            _cancellationRegistration = Token.UnsafeRegister(
                static state => ((DuoGeneration)state!).MarkInvalidated(),
                this);
        }

        public uint ProcessId { get; }
        public DuoApiConnection Connection { get; }
        public IDuoWebManager API => Connection.API;
        public DuoInstance[] Instances { get; }
        public CancellationToken Token { get; }
        public bool IsInvalidated => Volatile.Read(ref _invalidated) != 0;
        public Task WhenIdle => _idle.Task;

        public IDisposable? TryBeginOperation()
        {
            if (IsInvalidated)
                return null;

            Interlocked.Increment(ref _operationCount);

            if (!IsInvalidated)
                return new Operation(this);

            EndOperation();
            return null;
        }

        public void Invalidate()
        {
            MarkInvalidated();
            _cancellation.Cancel();
        }

        public void ReleaseLifetimeRegistration()
            => Interlocked.Exchange(ref _lifetimeRegistration, null)?.Dispose();

        private void MarkInvalidated()
        {
            Interlocked.Exchange(ref _invalidated, 1);

            if (Volatile.Read(ref _operationCount) == 0)
                _idle.TrySetResult(true);
        }

        private void EndOperation()
        {
            if (Interlocked.Decrement(ref _operationCount) == 0 && IsInvalidated)
                _idle.TrySetResult(true);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _cancellationRegistration.Dispose();
            _cancellation.Dispose();
        }

        private sealed class Operation(DuoGeneration owner) : IDisposable
        {
            private DuoGeneration? _owner = owner;

            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndOperation();
        }
    }
}
