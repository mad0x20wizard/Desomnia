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

            if (generationOperation == null)
                return;

            using var timeoutSource = new CancellationTokenSource();
            timeoutSource.CancelAfter(timeout);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                generation.Token,
                timeoutSource.Token);

            try
            {
                await instance.CommandSemaphore.WaitAsync(operation.Token);
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (instance.IsRunning == running)
                        return;

                    Logger.LogInformation(
                        "{operation} {instance}...",
                        running ? "Starting" : "Stopping",
                        instance.ToString());

                    using var changed = instance.ObserveState(running);
                    if (instance.IsRunning == running)
                        return;

                    if (running)
                        await generation.API.StartInstance(instance.Name, operation.Token);
                    else
                        await generation.API.StopInstance(instance.Name, operation.Token);

                    await changed.WaitAsync(operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                }
                finally
                {
                    instance.CommandSemaphore.Release();
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
