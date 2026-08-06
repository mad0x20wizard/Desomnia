using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Service.Duo.Manager;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SunshineServiceContextAdapter(DuoManager manager) : SunshineServiceAdapter, INetworkService
    {
        public required ILogger<SunshineServiceContextAdapter> Logger { get; set; }

        public required NetworkContext Context { private get; init; }

        private NetworkHostContext LocalHostContext => Context.First(ctx => ctx.Host is LocalHost);

        readonly Dictionary<DuoInstance, SunshineServiceContext> _contexts = [];

        async Task INetworkService.Startup()
        {
            LocalHostContext.Watch?.InspectionFilter += IsNotSunshineServiceWatch;

            // subscribe first — a Started fired mid-WatchInstances is then
            // deduplicated by the ContainsKey guard instead of being missed
            manager.Started += WatchInstances;
            manager.Stopped += UnWatchInstances;

            WatchInstances();
        }

        private void WatchInstances(object? sender = null, EventArgs? e = null)
        {
            foreach (var instance in manager) using (Context.Network.Mutex.Lock())
            {
                if (_contexts.ContainsKey(instance))
                    continue; // Startup() raced manager.Started for the same generation

                try
                {
                    Logger.LogInformation($"Monitoring {instance}:{instance.Port}" + (instance.IsRunning == true ? " (running)" : ""));

                    var context = LocalHostContext.CreateWatchedService<SunshineServiceContext>
                    (
                        TypedParameter.From(instance.Service), 
                        TypedParameter.From(instance.Info.MinStreamTraffic)
                    );

                    RegisterWatch(instance, context.Watch);

                    _contexts.Add(instance, context);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"NOT Monitoring {instance}:{instance.Port} -> could not create service context");
                }
            }
        }

        private bool IsNotSunshineServiceWatch(NetworkServiceWatch watch) => watch.Service is not SunshineService;

        private void UnWatchInstances(object? sender = null, EventArgs? e = null)
        {
            using (Context.Network.Mutex.Lock())
            {
                foreach ((var instance, var ctx) in _contexts)
                {
                    UnregisterWatch(instance, ctx.Watch);

                    ctx.Dispose();
                }

                _contexts.Clear();
            }
        }

        async Task INetworkService.Shutdown(NetworkShutdownReason reason)
        {
            LocalHostContext.Watch?.InspectionFilter -= IsNotSunshineServiceWatch;

            manager.Stopped -= UnWatchInstances;
            manager.Started -= WatchInstances;

            UnWatchInstances();
        }
    }
}