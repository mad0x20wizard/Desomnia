using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public abstract partial class DuoManager
    {
        public Task Start(DuoInstance instance, int timeout = DEFAULT_TIMEOUT, CancellationToken cancellationToken = default)
            => ChangeState(instance, running: true, timeout, cancellationToken);

        public Task Stop(DuoInstance instance, int timeout = 5000, CancellationToken cancellationToken = default)
            => ChangeState(instance, running: false, timeout, cancellationToken);

        private async Task ChangeState(
            DuoInstance instance,
            bool running,
            int timeout,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(instance);

            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            var generation = Volatile.Read(ref _generation);

            if (!Owns(generation, instance))
            {
                Logger.LogWarning(
                    "Cannot {operation} {instance} -> its Duo service generation is not running.",
                    running ? "start" : "stop",
                    instance);
                return;
            }

            using var generationOperation = generation!.TryBeginOperation();

            if (generationOperation == null || !Owns(generation, instance))
                return;

            using var timeoutSource = new CancellationTokenSource();
            timeoutSource.CancelAfter(timeout);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                generation.Token,
                timeoutSource.Token);
            var semaphoreEntered = false;

            try
            {
                await instance.Semaphore.WaitAsync(operation.Token);
                semaphoreEntered = true;

                if (!Owns(generation, instance) || instance.IsRunning == running)
                    return;

                Logger.LogInformation(
                    "{operation} {instance}...",
                    running ? "Starting" : "Stopping",
                    instance.ToString());

                var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void InstanceChanged(bool state)
                {
                    if (state == running)
                        changed.TrySetResult(true);
                }

                instance.RunningStateChanged += InstanceChanged;

                try
                {
                    if (instance.IsRunning == running || !Owns(generation, instance))
                        return;

                    if (running)
                        await generation.API.StartInstance(instance.Name, operation.Token);
                    else
                        await generation.API.StopInstance(instance.Name, operation.Token);

                    if (instance.IsRunning != running)
                        await changed.Task.WaitAsync(operation.Token);

                    operation.Token.ThrowIfCancellationRequested();

                    if (!Owns(generation, instance))
                        throw new OperationCanceledException("The Duo service generation changed.", generation.Token);
                }
                finally
                {
                    instance.RunningStateChanged -= InstanceChanged;
                }
            }
            catch (OperationCanceledException ex) when (
                timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested &&
                !generation.Token.IsCancellationRequested)
            {
                throw new System.TimeoutException(
                    $"Timed out while {(running ? "starting" : "stopping")} {instance}.",
                    ex);
            }
            catch (OperationCanceledException) when (
                generation.Token.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                Logger.LogDebug(
                    "Aborted {operation} for {instance} because its Duo service generation ended.",
                    running ? "start" : "stop",
                    instance);
            }
            finally
            {
                if (semaphoreEntered)
                    instance.Semaphore.Release();
            }
        }

        private bool Owns(DuoGeneration? generation, DuoInstance instance)
        {
            return generation is { IsInvalidated: false }
                && ReferenceEquals(Volatile.Read(ref _generation), generation)
                && !instance.IsDisposed
                && Array.IndexOf(generation.Instances, instance) >= 0;
        }
    }
}
