using MadWizard.Desomnia.Configuration;
using MadWizard.Desomnia.Service.Duo.Manager;
using MadWizard.Desomnia.Service.Duo.Manager.Watcher;
using MadWizard.Desomnia.Session;
using MadWizard.Desomnia.Session.Configuration;

namespace MadWizard.Desomnia.Service.Duo
{
    internal class DuoServiceContext : IDisposable
    {
        public required DuoSettings Settings { get; init; }

        public required IDuoManager Manager { get; init; }
        public required IDuoSessionWatcher Watcher { get; init; }

        public required IEnumerable<DuoInstance> Instances { get; init; }

        public required SessionMonitor SessionMonitor { private get; init; }
        public required SessionMonitorConfig SessionMonitorConfig { private get; init; }

        readonly CancellationTokenSource _lifetime = new();

        public async Task StartWatching(TimeSpan timeout = default)
        {
            using var startup = _lifetime.WithTimeout(timeout);

            await SessionMonitor.StartupFinished.WaitAsync(startup.Token);

            SessionMonitor.InspectionFilter += SessionMonitor_InspectionFilter;

            Watcher.SessionChanged += Watcher_SessionChanged;

            try
            {
                await Watcher.StartWatch(Instances, startup.Token);
            }
            catch (OperationCanceledException ex) when (!_lifetime.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out while starting to watch instances.", ex);
            }
        }

        // The Duo instance inspects its associated watch and supplies the final idle/usage result.
        private bool SessionMonitor_InspectionFilter(SessionWatch watch) => !watch.IsMonitoredBy<DuoInstance>();

        private void Watcher_SessionChanged(object? sender, InstanceSessionChangedEventArgs args)
        {
            args.Instance.StopTracking<SessionWatch>();

            if (SessionMonitor.TakeSnapshot().FirstOrDefault(w => w.Session == args.Session) is SessionWatch watch)
            {
                var instance = args.Instance;

                instance.Watch = watch.Watch << instance.Info.Watch;

                watch.ApplyConfiguration(SessionMonitorConfig, instance.Info with
                {
                    Watch = WatchExpression.Yield,
                    OnIdle = null // handled by the DuoInstance
                });

                instance.StartTracking(watch);
            }
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

                        void Watcher_SessionChanged(object? sender, InstanceSessionChangedEventArgs args)
                        {
                            if (instance == args.Instance && args.IsRunning == running)
                            {
                                args.Manually = false;

                                semaphore.Release();
                            }
                        }

                        Watcher.SessionChanged += Watcher_SessionChanged;

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
                            Watcher.SessionChanged -= Watcher_SessionChanged;
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

            try
            {
                Watcher.StopWatch();
            }
            finally
            {
                Watcher.SessionChanged -= Watcher_SessionChanged;

                SessionMonitor.InspectionFilter -= SessionMonitor_InspectionFilter;

                foreach (var instance in Instances)
                {
                    instance.StopTracking<SessionWatch>();
                }
            }
        }
    }
}
