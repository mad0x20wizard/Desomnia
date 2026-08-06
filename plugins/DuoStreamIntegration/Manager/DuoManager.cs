using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Service.Duo.Configuration;
using MadWizard.Desomnia.Session.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Refit;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public abstract class DuoManager(DuoSessionMonitorConfig config) : BackgroundService, IIEnumerable<DuoInstance>
    {
        const int DEFAULT_TIMEOUT = 30000;

        const string REGISTRY_KEY = "SOFTWARE\\Duo";
        const ushort DEFAULT_PORT = 38299;

        internal IDuoWebManager? API;

        public required ILogger<DuoManager> Logger { get; set; }

        public required ISessionManager SessionManager { private get; init; }

        public required Func<DuoInstanceInfo, RegistryKey, DuoInstance> CreateInstance { private get; init; }

        protected ServiceController Service => field ??= new(config.ServiceName);

        private IList<DuoInstance> Instances { get; set; } = [];

        public event EventHandler? Started;
        public event EventHandler? Stopped;

        private ushort Port
        {
            get
            {
                using var duo = Registry.LocalMachine!.OpenSubKey(REGISTRY_KEY);

                return duo?.GetValue("Port") is int port ? (ushort)port : DEFAULT_PORT;
            }
        }

        protected uint? ServicePID { get; private set; }

        protected async Task TriggerStarted(uint? servicePID = null)
        {
            var servicePath = Service.ExecutablePath;
            var serviceVersion = Service.Version;

            ServicePID = servicePID ?? Service.PID;

            Logger.LogInformation("Service is running at: '{path}' ({version}) -> PID {pid}", servicePath, serviceVersion, ServicePID);

            API = RestService.For<IDuoWebManager>("http://localhost:" + Port);

            // externally owned — dispose the replaced generation, but only after its
            // successor is published: adapters claim watches concurrently and must
            // never adopt into an already-disposed instance
            var stale = Instances;

            Instances = LoadInstances();

            foreach (var old in stale)
                old.Dispose();

            await TriggerRefresh();

            this.Started?.Invoke(this, EventArgs.Empty);

            SessionManager.UserLogon += SessionManager_UserLogon;
            SessionManager.UserLogoff += SessionManager_UserLogoff;
        }

        protected async Task TriggerRefresh()
        {
            if (API is not IDuoWebManager api)
                return; // the service stopped concurrently

            foreach (var instance in this) using (await instance.RefreshMutex.LockAsync())
            {
                if (instance.IsDisposed)
                    continue; // a dying generation — don't touch its registry key

                bool? wasRunning = instance.IsRunning, shouldBeRunning = await api.QueryInstance(instance.Name);

                if (shouldBeRunning.Value)
                    instance.IsRunning = instance.IsSandboxed || (instance.SessionID != null);
                else
                {
                    instance.IsRunning = false;
                    instance.SessionID = null;
                }

                if (wasRunning != null && wasRunning != instance.IsRunning)
                {
                    Logger.LogInformation($"{instance} is now {(instance.IsRunning.Value ? "running" : "stopped")} " +
                        $"{(instance.IsBusy ? "" : "(manually)")}");
                }
            }
        }

        protected virtual void TriggerStopped()
        {
            SessionManager.UserLogoff -= SessionManager_UserLogoff;
            SessionManager.UserLogon -= SessionManager_UserLogon;

            this.Stopped?.Invoke(this, EventArgs.Empty);

            // swap instead of Clear() — enumerators handed out to other threads
            // (inspection loop, session events) must never see in-place mutation
            var stale = Instances;

            Instances = [];

            foreach (var instance in stale)
                instance.Dispose();

            ServicePID = null;

            API = null;
        }

        protected List<DuoInstance> LoadInstances()
        {
            using RegistryKey instancesKey = OpenInstancesKey();

            var instances = new List<DuoInstance>();

            try
            {
                foreach (var name in instancesKey.GetSubKeyNames())
                {
                    var info = config[name] ?? new DuoInstanceInfo { Name = name };

                    info.OnDemand   ??= config.OnInstanceDemand;
                    info.OnIdle     ??= config.OnInstanceIdle;

                    info.OnLogin    ??= config.OnInstanceLogin;
                    info.OnStart    ??= config.OnInstanceStarted;
                    info.OnStop     ??= config.OnInstanceStopped;
                    info.OnLogout   ??= config.OnInstanceLogout;

                    info.PreventIdleIfStreaming ??= config.PreventIdleIfStreaming;

                    var instance = CreateInstance(info, instancesKey.OpenSubKey(name!, writable: true)!);

                    instance.Session = SessionManager.FirstOrDefault(instance.HasInitiated);

                    instances.Add(instance);
                }
            }
            catch
            {
                // a failed generation is never published — dispose the partial build
                // (open writable registry keys!), or every adoption retry leaks a batch
                foreach (var instance in instances)
                    instance.Dispose();

                throw;
            }

            return instances;
        }

        public async Task Start(DuoInstance instance, int timeout = DEFAULT_TIMEOUT)
        {
            if (API is not IDuoWebManager api)
            {
                Logger.LogWarning($"Cannot start {instance} -> Duo service is not running.");

                return;
            }

            Logger.LogInformation($"Starting {instance}...");

            await api.StartInstance(instance.Name);

            if (instance.IsRunning != true)
            {
                var semaphore = new SemaphoreSlim(0);

                async Task Instance_Started(Event data)
                {
                    semaphore.Release();
                }

                instance.Started += Instance_Started;

                try
                {
                    await semaphore.WaitAsync(timeout);
                }
                finally
                {
                    instance.Started -= Instance_Started;
                }
            }
        }

        public async Task Stop(DuoInstance instance, int timeout = 5000)
        {
            if (API is not IDuoWebManager api)
            {
                Logger.LogWarning($"Cannot stop {instance} -> Duo service is not running.");

                return;
            }

            Logger.LogInformation($"Stopping {instance}...");

            await api.StopInstance(instance.Name);

            if (instance.IsRunning != false)
            {
                var semaphore = new SemaphoreSlim(0);

                async Task Instance_Stopped(Event data)
                {
                    semaphore.Release();
                }

                instance.Stopped += Instance_Stopped;

                try
                {
                    await semaphore.WaitAsync(timeout);
                }
                finally
                {
                    instance.Stopped -= Instance_Stopped;
                }
            }
        }

        #region SessionManager events
        private void SessionManager_UserLogon(object? sender, ISession session)
        {
            if (this.FirstOrDefault(instance => instance.HasInitiated(session)) is DuoInstance instance)
                instance.Session = session;
        }
        private void SessionManager_UserLogoff(object? sender, ISession session)
        {
            if (this.FirstOrDefault(instance => instance.Session == session) is DuoInstance instance)
                instance.Session = null;
        }
        #endregion

        public IEnumerator<DuoInstance> GetEnumerator()
        {
            return Instances.GetEnumerator();
        }

        public override void Dispose()
        {
            Service.Dispose();

            base.Dispose();
        }
        
        private static RegistryKey OpenInstancesKey()
        {
            using RegistryKey? duo = Registry.LocalMachine!.OpenSubKey(REGISTRY_KEY);

            if (duo != null)
            {
                RegistryKey? instances = duo.OpenSubKey("Instances"); // since Duo 1.5.0

                return instances ?? duo;
            }

            throw new FileNotFoundException(fileName: REGISTRY_KEY, message: "Duo registry key not found");
        }
    }

    internal interface IDuoWebManager
    {
        [Get("/instances/{name}")]
        Task<bool> QueryInstance(string name);

        [Get("/instances/{name}/start")]
        Task StartInstance(string name);

        [Get("/instances/{name}/stop")]
        Task StopInstance(string name);
    }

}
