using MadWizard.Desomnia.Service.Duo.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Refit;
using System.ServiceProcess;

namespace MadWizard.Desomnia.Service.Duo.Manager
{
    public abstract class DuoManager(DuoStreamMonitorConfig config) : BackgroundService, IIEnumerable<DuoInstance>
    {
        const int DEFAULT_TIMEOUT = 30000;

        const string REGISTRY_KEY = "SOFTWARE\\Duo";
        const ushort DEFAULT_PORT = 38299;

        public required ILogger<DuoManager> Logger { get; set; }

        internal IDuoWebManager? API;

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

        protected async Task TriggerStarted(uint? servicePID = null)
        {
            var servicePath = Service.GetExecutablePath();
            var serviceVersion = Service.GetVersion();
            servicePID ??= Service.GetPID();

            Logger.LogInformation("Service is running at: '{path}' ({version}) -> PID {pid}", servicePath, serviceVersion, servicePID);

            API = RestService.For<IDuoWebManager>("http://localhost:" + Port);

            Instances = LoadInstances();

            await TriggerRefresh();

            this.Started?.Invoke(this, EventArgs.Empty);
        }

        protected async Task TriggerRefresh()
        {
            foreach (var instance in this) using (await instance.RefreshMutex.LockAsync())
            {
                bool? wasRunning = instance.IsRunning, shouldBeRunning = await API!.QueryInstance(instance.Name);

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

        protected void TriggerStopped()
        {
            this.Stopped?.Invoke(this, EventArgs.Empty);

            foreach (var instance in this)
                instance.Dispose();

            Instances.Clear();

            API = null;
        }

        protected List<DuoInstance> LoadInstances()
        {
            using RegistryKey instancesKey = OpenInstancesKey();

            var instances = new List<DuoInstance>();
            foreach (var name in instancesKey.GetSubKeyNames())
            {
                var info = config[name] ?? new DuoInstanceInfo(name);

                info.OnDemand ??= config.OnInstanceDemand;
                info.OnIdle ??= config.OnInstanceIdle;

                info.OnLogin ??= config.OnInstanceLogin;
                info.OnStart ??= config.OnInstanceStarted;
                info.OnStop ??= config.OnInstanceStopped;
                info.OnLogout ??= config.OnInstanceLogout;

                instances.Add(new DuoInstance(info, instancesKey.OpenSubKey(name!, writable: true)!));
            }

            return instances;
        }

        public async Task Start(DuoInstance instance, int timeout = DEFAULT_TIMEOUT)
        {
            Logger.LogInformation($"Starting {instance}...");

            await API!.StartInstance(instance.Name);

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
            Logger.LogInformation($"Stopping {instance}...");

            await API!.StopInstance(instance.Name);

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
