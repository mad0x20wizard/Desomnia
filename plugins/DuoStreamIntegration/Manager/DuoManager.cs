using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nito.AsyncEx;
using Refit;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public abstract partial class DuoManager(DuoSessionMonitorConfig config) : BackgroundService, IIEnumerable<DuoInstance>
    {
        private const int DEFAULT_TIMEOUT = 30000;
        private const string REGISTRY_KEY = "SOFTWARE\\Duo";
        private const ushort DEFAULT_PORT = 38299;
        private static readonly TimeSpan REFRESH_TIMEOUT = TimeSpan.FromSeconds(5);

        private readonly AsyncLock _lifecycleMutex = new();
        private readonly object _sessionSubscriptionMutex = new();
        private DuoGeneration? _generation;
        private ServiceController? _service;
        private bool _sessionsSubscribed;
        private int _disposed;

        public required ILogger<DuoManager> Logger { get; set; }

        public required ISessionManager SessionManager { private get; init; }

        public required Func<DuoInstanceInfo, RegistryKey, DuoInstance> CreateInstance { private get; init; }

        protected ServiceController Service => _service ??= new(config.ServiceName);

        protected uint? ServicePID => Volatile.Read(ref _generation)?.ProcessId;

        protected bool IsGenerationInvalidated => Volatile.Read(ref _generation) is { IsInvalidated: true };

        public event EventHandler<DuoLifecycleEventArgs>? Started;
        public event EventHandler<DuoLifecycleEventArgs>? Stopped;

        private ushort Port
        {
            get
            {
                using var duo = Registry.LocalMachine!.OpenSubKey(REGISTRY_KEY);
                return duo?.GetValue("Port") is int port ? (ushort)port : DEFAULT_PORT;
            }
        }

        internal virtual DuoApiConnection CreateAPI()
        {
            var client = new HttpClient { BaseAddress = new Uri("http://localhost:" + Port) };

            try
            {
                return new DuoApiConnection(RestService.For<IDuoWebManager>(client), client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        internal virtual (string Path, Version Version) GetServiceInfo() => (Service.ExecutablePath, Service.Version);

        protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            finally
            {
                UnsubscribeSessionEvents();
                await TriggerStopped(notify: Volatile.Read(ref _disposed) == 0);
            }
        }

        protected abstract Task RunAsync(CancellationToken stoppingToken);

        protected async Task<bool> TriggerStarted(
            uint? servicePID = null,
            CancellationToken lifetimeToken = default,
            IDisposable? lifetimeRegistration = null)
        {
            var processId = servicePID ?? Service.PID
                ?? throw new InvalidOperationException("The running Duo service has no process ID.");
            IDisposable? pendingRegistration = lifetimeRegistration;
            DuoApiConnection? pendingConnection = null;
            DuoInstance[]? pendingInstances = null;
            DuoGeneration? candidate = null;

            try
            {
                while (true)
                {
                    DuoGeneration? conflict;
                    DuoGeneration? started = null;

                    using (await _lifecycleMutex.LockAsync(lifetimeToken))
                    {
                        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                        lifetimeToken.ThrowIfCancellationRequested();

                        var current = Volatile.Read(ref _generation);

                        if (current is { IsInvalidated: false } && current.ProcessId == processId)
                            return false;

                        conflict = current;

                        if (conflict == null)
                        {
                            var (servicePath, serviceVersion) = GetServiceInfo();
                            pendingInstances = LoadInstances().ToArray();
                            pendingConnection = CreateAPI();

                            candidate = new DuoGeneration(
                                processId,
                                pendingConnection,
                                pendingInstances,
                                lifetimeToken,
                                pendingRegistration);
                            pendingConnection = null;
                            pendingInstances = null;
                            pendingRegistration = null;

                            await RefreshGeneration(candidate, candidate.Token, requireCurrent: false, []);
                            candidate.Token.ThrowIfCancellationRequested();

                            Logger.LogInformation(
                                "Service is running at: '{path}' ({version}) -> PID {pid}",
                                servicePath,
                                serviceVersion,
                                processId);

                            started = candidate;
                            Volatile.Write(ref _generation, started);
                            candidate = null;
                        }
                    }

                    if (conflict != null)
                    {
                        await RetireGeneration(conflict, notify: true);
                        continue;
                    }

                    if (started!.IsInvalidated)
                    {
                        await RetireGeneration(started, notify: false);
                        return false;
                    }

                    Notify(Started, "started", started);
                    return true;
                }
            }
            finally
            {
                DisposeSafely(pendingRegistration, "Duo generation lifetime registration");
                DisposeSafely(pendingConnection, "Duo API connection");

                if (candidate != null)
                    await DestroyGeneration(candidate);

                if (pendingInstances != null)
                    DisposeInstances(pendingInstances);
            }
        }

        protected async Task TriggerRefresh(CancellationToken cancellationToken = default)
        {
            var generation = Volatile.Read(ref _generation);

            if (generation is null or { IsInvalidated: true })
                return;

            using var generationOperation = generation.TryBeginOperation();

            if (generationOperation == null)
                return;

            var changes = new List<StateChange>();
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, generation.Token);

            try
            {
                using (await _lifecycleMutex.LockAsync(operation.Token))
                {
                    if (!ReferenceEquals(Volatile.Read(ref _generation), generation) || generation.IsInvalidated)
                        return;

                    await RefreshGeneration(generation, operation.Token, requireCurrent: true, changes);
                }
            }
            finally
            {
                PublishStateChanges(generation, changes);
            }
        }

        protected Task<bool> TriggerStopped(uint? expectedProcessId = null)
        {
            var generation = Volatile.Read(ref _generation);

            if (generation == null || expectedProcessId is uint expected && generation.ProcessId != expected)
                return Task.FromResult(false);

            return RetireGeneration(generation, notify: true);
        }

        private Task<bool> TriggerStopped(bool notify)
        {
            var generation = Volatile.Read(ref _generation);
            return generation == null ? Task.FromResult(false) : RetireGeneration(generation, notify);
        }

        private async Task<bool> RetireGeneration(DuoGeneration generation, bool notify)
        {
            using (await _lifecycleMutex.LockAsync())
            {
                if (!ReferenceEquals(Volatile.Read(ref _generation), generation))
                    return false;

                Volatile.Write(ref _generation, null);
                InvalidateSafely(generation);
                ReleaseLifetimeSafely(generation);
            }

            if (notify)
                Notify(Stopped, "stopped", generation);

            await DestroyGeneration(generation);
            return true;
        }

        private async Task RefreshGeneration(
            DuoGeneration generation,
            CancellationToken cancellationToken,
            bool requireCurrent,
            List<StateChange> changes)
        {
            foreach (var instance in generation.Instances)
            {
                using (await instance.RefreshMutex.LockAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (requireCurrent && !ReferenceEquals(Volatile.Read(ref _generation), generation))
                        throw new OperationCanceledException("The Duo service generation changed.", cancellationToken);

                    if (instance.IsDisposed)
                        continue;

                    bool isReportedRunning;
                    using (var timeout = new CancellationTokenSource(REFRESH_TIMEOUT))
                    using (var query = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
                    {
                        try
                        {
                            isReportedRunning = await generation.API.QueryInstance(instance.Name, query.Token);
                        }
                        catch (OperationCanceledException ex) when (
                            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            throw new System.TimeoutException($"Timed out while refreshing {instance}.", ex);
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (requireCurrent && !ReferenceEquals(Volatile.Read(ref _generation), generation))
                        throw new OperationCanceledException("The Duo service generation changed.", cancellationToken);

                    if (instance.IsDisposed)
                        continue;

                    var wasRunning = instance.IsRunning;
                    bool isRunning;

                    if (isReportedRunning)
                    {
                        isRunning = instance.IsSandboxed || instance.SessionID != null;
                    }
                    else
                    {
                        instance.SessionID = null;
                        isRunning = false;
                    }

                    if (instance.SetRunningState(isRunning))
                        changes.Add(new StateChange(instance, isRunning));

                    if (wasRunning != null && wasRunning != isRunning)
                    {
                        Logger.LogInformation(
                            "{instance} is now {state} {source}",
                            instance.ToString(),
                            isRunning ? "running" : "stopped",
                            instance.IsBusy ? "" : "(manually)");
                    }
                }
            }
        }

        private void PublishStateChanges(DuoGeneration generation, IEnumerable<StateChange> changes)
        {
            foreach (var change in changes)
            {
                if (!Owns(generation, change.Instance) || change.Instance.IsRunning != change.Running)
                    continue;

                var generationOperation = generation.TryBeginOperation();

                if (generationOperation == null)
                    continue;

                Task notification;

                try
                {
                    notification = change.Instance.TriggerRunningStateChangedAsync(change.Running);
                }
                catch (Exception ex)
                {
                    generationOperation.Dispose();
                    LogSafely(ex, "Could not publish the state change for {instance}", change.Instance);
                    continue;
                }

                _ = ObserveStateChange(notification, generationOperation, change.Instance);
            }
        }

        private async Task ObserveStateChange(Task notification, IDisposable generationOperation, DuoInstance instance)
        {
            try
            {
                await notification;
            }
            catch (Exception ex)
            {
                LogSafely(ex, "Could not publish the state change for {instance}", instance);
            }
            finally
            {
                generationOperation.Dispose();
            }
        }

        private async Task DestroyGeneration(DuoGeneration generation)
        {
            InvalidateSafely(generation);
            ReleaseLifetimeSafely(generation);
            await generation.WhenIdle;

            DisposeInstances(generation.Instances);
            DisposeSafely(generation.Connection, "Duo API connection");
            DisposeSafely(generation, "Duo generation cancellation source");
        }

        private void DisposeInstances(IEnumerable<DuoInstance> instances)
        {
            foreach (var instance in instances)
                DisposeSafely(instance, instance.ToString());
        }

        private void InvalidateSafely(DuoGeneration generation)
        {
            try
            {
                generation.Invalidate();
            }
            catch (Exception ex)
            {
                LogSafely(ex, "Could not cancel Duo generation {pid}", generation.ProcessId);
            }
        }

        private void ReleaseLifetimeSafely(DuoGeneration generation)
        {
            try
            {
                generation.ReleaseLifetimeRegistration();
            }
            catch (Exception ex)
            {
                LogSafely(ex, "Could not release Duo generation {pid}", generation.ProcessId);
            }
        }

        private void DisposeSafely(IDisposable? resource, string description)
        {
            if (resource == null)
                return;

            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                LogSafely(ex, "Could not dispose {resource}", description);
            }
        }

        private void LogSafely(Exception exception, string message, params object?[] args)
        {
            try
            {
                Logger.LogError(exception, message, args);
            }
            catch
            {
                // Cleanup must continue even if the logging pipeline is already gone.
            }
        }

        private void Notify(
            EventHandler<DuoLifecycleEventArgs>? subscribers,
            string transition,
            DuoGeneration generation)
        {
            if (subscribers == null)
                return;

            var args = new DuoLifecycleEventArgs(
                generation.ProcessId,
                Array.AsReadOnly(generation.Instances));

            foreach (EventHandler<DuoLifecycleEventArgs> subscriber in subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(this, args);
                }
                catch (Exception ex)
                {
                    LogSafely(ex, "Duo service {transition} subscriber failed", transition);
                }
            }
        }

        protected virtual List<DuoInstance> LoadInstances()
        {
            using RegistryKey instancesKey = OpenInstancesKey();
            var instances = new List<DuoInstance>();

            try
            {
                foreach (var name in instancesKey.GetSubKeyNames())
                {
                    var info = config[name] ?? new DuoInstanceInfo { Name = name };

                    info.OnDemand ??= config.OnInstanceDemand;
                    info.OnIdle ??= config.OnInstanceIdle;
                    info.OnLogin ??= config.OnInstanceLogin;
                    info.OnStart ??= config.OnInstanceStarted;
                    info.OnStop ??= config.OnInstanceStopped;
                    info.OnLogout ??= config.OnInstanceLogout;
                    info.WatchStreamTraffic ??= config.WatchStreamTraffic;

                    var instance = CreateInstance(info, instancesKey.OpenSubKey(name, writable: true)!);
                    instance.Session = SessionManager.FirstOrDefault(instance.HasInitiated);
                    instances.Add(instance);
                }
            }
            catch
            {
                DisposeInstances(instances);
                throw;
            }

            return instances;
        }

        public IEnumerator<DuoInstance> GetEnumerator()
        {
            var instances = Volatile.Read(ref _generation)?.Instances ?? [];
            return ((IEnumerable<DuoInstance>)instances).GetEnumerator();
        }

        private static RegistryKey OpenInstancesKey()
        {
            RegistryKey? duo = Registry.LocalMachine!.OpenSubKey(REGISTRY_KEY);

            if (duo == null)
                throw new FileNotFoundException(fileName: REGISTRY_KEY, message: "Duo registry key not found");

            var instances = duo.OpenSubKey("Instances");

            if (instances != null)
            {
                duo.Dispose();
                return instances;
            }

            return duo;
        }

        private readonly record struct StateChange(DuoInstance Instance, bool Running);

    }
}
