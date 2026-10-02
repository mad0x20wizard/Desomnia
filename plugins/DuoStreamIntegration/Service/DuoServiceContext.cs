using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx.Synchronous;
using System.Collections.Concurrent;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoServiceContext : IDisposable
    {
        public required ILogger<DuoServiceContext> Logger { protected get; init; }

        public required IDuoManager Manager { get; init; }
        public required IDuoWatcher Watcher { get; init; }

        public required IEnumerable<DuoInstance> Instances { get; init; }

        readonly CancellationTokenSource _lifetime = new();

        readonly ConcurrentDictionary<DuoInstance, StateChangeRequest> _requests = [];

        public async Task InitializeAsync(TimeSpan timeout = default)
        {
            using var init = _lifetime.WithTimeout(timeout);

            try
            {
                foreach (var instance in Instances)
                {
                    instance.IsRunning = await Manager.QueryState(instance, init.Token);
                }
            }
            catch (OperationCanceledException ex) when (!_lifetime.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out while initializing instances.", ex);
            }
        }

        #region Watching
        private Task? _watchTask;

        public void StartWatching()
        {
            _watchTask = WatchAsync(_lifetime.Token);
        }

        private async Task WatchAsync(CancellationToken token)
        {
            try
            {
                await foreach (var signal in Watcher.WatchAsync(Instances, token))
                {
                    foreach (var instance in signal.Instance is null ? Instances : [signal.Instance])
                    {
                        try
                        {
                            bool isRunning = signal.IsRunning ?? await Manager.QueryState(instance, token);

                            if (instance.IsRunning != isRunning)
                            {
                                /**
                                 * We only want to notify about the change, 
                                 * when both the Duo state and the session state match.
                                 */
                                if (isRunning == (instance.Session is not null))
                                {
                                    if (_requests.TryGetValue(instance, out var request))
                                    {
                                        request.Notify(isRunning); // maybe release request
                                    }

                                    Logger.LogInformation($"{instance} is now " +
                                        $"{(isRunning ? "running" : "stopped")} " +
                                        $"{(request is null ? "(manually)" : "")}");

                                    instance.IsRunning = isRunning;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Could not query/update instance {Name}", instance.Name);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // watch has ended normally.
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not watch instances");
            }
        }

        public void StopWatching()
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _lifetime.Cancel();

                _watchTask?.WaitAndUnwrapException();
                _watchTask = null;
            }
        }
        #endregion

        #region StateChangeRequest handling
        public async Task Start(DuoInstance instance, TimeSpan timeout = default) =>
            await HandleStateRequest(instance, new(true, timeout));

        public async Task Stop(DuoInstance instance, TimeSpan timeout = default) => 
            await HandleStateRequest(instance, new(false, timeout));

        private async Task HandleStateRequest(DuoInstance instance, StateChangeRequest request)
        {
            if (!Instances.Contains(instance))
                throw new InvalidOperationException($"Received request for instance '{instance.Name}' out of context.");

            using var cancellation = _lifetime.WithTimeout(request.Timeout);

            try
            {
                using (await instance.Mutex.LockAsync(cancellation.Token))
                {
                    if (await Manager.QueryState(instance, cancellation.Token) != request.ShouldBeRunning)
                    {
                        using (_requests[instance] = request)
                        {
                            Logger.LogInformation("{Operation} {Instance}...",
                                request.ShouldBeRunning ? "Starting" : "Stopping",
                                instance.ToString());

                            try
                            {
                                await Manager.RequestState(instance, request.ShouldBeRunning, cancellation.Token);

                                await request.WaitAsync(cancellation.Token);
                            }
                            finally
                            {
                                _requests.Remove(instance, out _);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                {
                    throw new TimeoutException($"Instance did not change it's state after {request.Timeout}.");
                }
            }
        }
        #endregion

        void IDisposable.Dispose()
        {
            using (_lifetime)
            {
                StopWatching();
            }
        }

        private class StateChangeRequest : IDisposable
        {
            readonly SemaphoreSlim _semaphore = new(0);

            internal bool ShouldBeRunning { get; init; }

            internal TimeSpan Timeout { get; init; }

            internal StateChangeRequest(bool running, TimeSpan timeout = default)
            {
                ShouldBeRunning = running;

                Timeout = timeout;
            }

            internal async Task WaitAsync(CancellationToken token)
            {
                await _semaphore.WaitAsync(token);
            }

            internal void Notify(bool isRunning)
            {
                if (isRunning == ShouldBeRunning)
                {
                    _semaphore.Release();
                }
            }

            void IDisposable.Dispose()
            {
                _semaphore.Dispose();
            }
        }
    }
}
