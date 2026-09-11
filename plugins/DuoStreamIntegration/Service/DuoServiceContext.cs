using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoServiceContext : IDisposable
    {
        public required DuoSettings Settings { get; init; }

        public required IDuoManager Manager { get; init; }
        public required IDuoInstanceWatcher Watcher { get; init; }

        public required IEnumerable<DuoInstance> Instances { get; init; }

        readonly CancellationTokenSource _lifetime = new();

        private Task? _watchTask;

        public void StartWatching(TimeSpan timeout = default)
        {
            using var startup = _lifetime.WithTimeout(timeout);

            foreach (var instance in Instances)
            {
                instance.IsRunning = Manager.QueryRunningState(instance, startup.Token).Result;
            }

            _watchTask = Watcher.WatchAsync(Instances, _lifetime.Token);
        }

        public async Task Start(DuoInstance instance, TimeSpan timeout = default) =>
            await HandleStateRequest(instance, true, timeout);

        public async Task Stop(DuoInstance instance, TimeSpan timeout = default) => 
            await HandleStateRequest(instance, false, timeout);

        private async Task HandleStateRequest(DuoInstance instance, bool running, TimeSpan timeout = default)
        {
            using var cancellation = _lifetime.WithTimeout(timeout);

            try
            {
                using (await instance.Mutex.LockAsync(cancellation.Token))
                {
                    if (Instances.Contains(instance) && instance.IsRunning != running)
                    {
                        var semaphore = new SemaphoreSlim(0);

                        async void Watcher_StatusChanged(object? sender, InstanceStatusChangedEventArgs args)
                        {
                            if (instance == args.Instance && args.Status == running)
                            {
                                args.Manually = false;

                                semaphore.Release();
                            }
                        }

                        Watcher.StatusChanged += Watcher_StatusChanged;

                        try
                        {
                            await Manager.ChangeState(instance, running, cancellation.Token);

                            if (instance.IsRunning != running)
                            {
                                await semaphore.WaitAsync(cancellation.Token);
                            }
                        }
                        finally
                        {
                            Watcher.StatusChanged -= Watcher_StatusChanged;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                {
                    throw new TimeoutException($"DuoInstance did not change it's state after {timeout}.");
                }
            }
        }

        void IDisposable.Dispose()
        {
            _lifetime.Cancel();

            _watchTask?.Wait();
        }
    }
}
