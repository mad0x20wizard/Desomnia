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

        // Lifecycle transitions and refreshes are sequential. Commands never acquire
        // this mutex: refresh must remain free to confirm their requested state.
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

        protected TimeSpan PollInterval => config.PollInterval;

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

        protected async Task Reconcile(uint? processId, CancellationToken stoppingToken)
        {
            if (processId is not uint runningProcessId)
            {
                await TriggerStopped();
            }
            else if (ServicePID == runningProcessId && !IsGenerationInvalidated)
            {
                await TriggerRefresh(stoppingToken);
            }
            else
            {
                if (ServicePID is uint previousProcessId && previousProcessId != runningProcessId)
                {
                    Logger.LogWarning(
                        "Duo service PID changed from {previousPID} to {currentPID} without an observed stop.",
                        previousProcessId, runningProcessId);
                }

                await TriggerStopped();
                await Adopt(runningProcessId, stoppingToken);
            }
        }

        protected virtual async Task Adopt(uint processId, CancellationToken stoppingToken)
            => await TriggerStarted(processId, stoppingToken);

        protected async Task<bool> TriggerStarted(
            uint? servicePID = null,
            CancellationToken lifetimeToken = default,
            IDisposable? lifetimeRegistration = null)
        {
            DuoGeneration? candidate = null;

            try
            {
                using (await _lifecycleMutex.LockAsync(lifetimeToken))
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    lifetimeToken.ThrowIfCancellationRequested();

                    var processId = servicePID ?? Service.PID
                        ?? throw new InvalidOperationException("The running Duo service has no process ID.");

                    if (_generation is { IsInvalidated: false } current && current.ProcessId == processId)
                        return false;

                    await RetireGeneration(notify: true);

                    var (servicePath, serviceVersion) = GetServiceInfo();
                    candidate = CreateGeneration(processId, lifetimeToken, lifetimeRegistration);
                    lifetimeRegistration = null; // now owned by the generation

                    await RefreshGeneration(candidate, candidate.Token, []);
                    candidate.Token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

                    Logger.LogInformation(
                        "Service is running at: '{path}' ({version}) -> PID {pid}",
                        servicePath, serviceVersion, processId);

                    var started = candidate;
                    Volatile.Write(ref _generation, started);
                    candidate = null;

                    if (started.IsInvalidated)
                    {
                        await RetireGeneration(notify: false);
                        return false;
                    }

                    // Lifecycle notifications share the transition ordering. Their consumers
                    // may issue commands, which do not acquire the lifecycle mutex.
                    Notify(Started, "started", started);
                    return true;
                }
            }
            finally
            {
                if (lifetimeRegistration != null)
                    DuoGeneration.DisposeResources(Logger, [lifetimeRegistration]);

                if (candidate != null)
                    await candidate.DisposeAsync();
            }
        }

        private DuoGeneration CreateGeneration(uint processId, CancellationToken token, IDisposable? registration)
        {
            var instances = LoadInstances().ToArray();
            DuoApiConnection? connection = null;

            try
            {
                connection = CreateAPI();
                return new DuoGeneration(processId, connection, instances, token, registration, Logger);
            }
            catch
            {
                DuoGeneration.DisposeResources(Logger, instances);
                if (connection != null)
                    DuoGeneration.DisposeResources(Logger, [connection]);
                throw;
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

                    await RefreshGeneration(generation, operation.Token, changes);
                }
            }
            finally
            {
                PublishStateChanges(generation, changes);
            }
        }

        protected async Task<bool> TriggerStopped(uint? expectedProcessId = null, bool notify = true)
        {
            using (await _lifecycleMutex.LockAsync())
            {
                if (_generation == null || expectedProcessId is uint expected && _generation.ProcessId != expected)
                    return false;

                await RetireGeneration(notify);
                return true;
            }
        }

        // Called only while holding the lifecycle mutex, including the drain. A successor
        // cannot publish its instances until the old generation has finished detaching.
        private async Task RetireGeneration(bool notify)
        {
            var generation = _generation;
            if (generation == null)
                return;

            Volatile.Write(ref _generation, null);
            await generation.CancelAsync();

            if (notify)
                Notify(Stopped, "stopped", generation);

            await generation.DisposeAsync();
        }

        private async Task RefreshGeneration(
            DuoGeneration generation,
            CancellationToken cancellationToken,
            List<StateChange> changes)
        {
            foreach (var instance in generation.Instances)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (instance.IsDisposed)
                    continue;

                var isReportedRunning = await QueryRunningState(generation, instance, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

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

        private async Task<bool> QueryRunningState(DuoGeneration generation, DuoInstance instance, CancellationToken token)
        {
            using var query = CancellationTokenSource.CreateLinkedTokenSource(token);
            query.CancelAfter(REFRESH_TIMEOUT);

            try
            {
                return await generation.API.QueryInstance(instance.Name, query.Token);
            }
            catch (OperationCanceledException ex) when (query.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new System.TimeoutException($"Timed out while refreshing {instance}.", ex);
            }
        }

        private void PublishStateChanges(DuoGeneration generation, IEnumerable<StateChange> changes)
        {
            foreach (var change in changes)
            {
                // Actions can await commands whose completion needs a later refresh.
                _ = PublishStateChange(generation, change);
            }
        }

        private async Task PublishStateChange(DuoGeneration generation, StateChange change)
        {
            using var operation = generation.TryBeginOperation();
            if (operation == null || change.Instance.IsDisposed || change.Instance.IsRunning != change.Running)
                return;

            try
            {
                await change.Instance.TriggerRunningStateChangedAsync(change.Running);
            }
            catch (Exception ex)
            {
                DuoGeneration.LogCleanupError(Logger, ex, "Could not publish the state change for {instance}", change.Instance);
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
                    DuoGeneration.LogCleanupError(Logger, ex, "Duo service {transition} subscriber failed", transition);
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
                DuoGeneration.DisposeResources(Logger, instances);
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
