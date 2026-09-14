using Autofac;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Ressource.Events;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Sunshine.Watch
{
    internal class SunshineServiceContextAdapter(DuoSessionMonitor monitor) : SunshineServiceAdapter, INetworkService
    {
        public required ILogger<SunshineServiceContextAdapter> Logger { get; set; }

        public required NetworkContext Context { private get; init; }

        private NetworkHostContext LocalHostContext => Context.First(ctx => ctx.Host is LocalHost);

        volatile Dictionary<DuoInstance, SunshineServiceContext>? _contexts;

        async Task INetworkService.Startup()
        {
            Logger.LogDebug("NetworkMonitor '{Name}' is connected, start watching instances:", Context.Name);

            Attach();

            _contexts = [];

            WatchInstances(monitor.TakeSnapshot());
        }

        private void Attach()
        {
            LocalHostContext.Watch?.InspectionFilter += IsNotSunshineServiceWatch;

            monitor.TrackingStarted += Monitor_TrackingStarted;
            monitor.TrackingStopped += Monitor_TrackingStopped;
        }

        private void Monitor_TrackingStarted(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            WatchInstances([args.Inspectable]);
        }

        private void WatchInstances(IEnumerable<DuoInstance> instances)
        {
            using (Context.Network.Mutex.Lock()) if (_contexts is not null)
            {
                foreach (var instance in instances.Where(i => !_contexts.ContainsKey(i)))
                {
                    try
                    {
                        Logger.LogInformation($"Monitoring {instance.ToString()}:{instance.Settings.Port}" 
                            + (instance.IsRunning == true ? " (running)" : ""));

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
                        Logger.LogError(ex, $"NOT Monitoring {instance.ToString()}:{instance.Settings.Port} -> could not create service context");
                    }
                }
            }
        }

        private bool IsNotSunshineServiceWatch(NetworkServiceWatch watch) => watch.Service is not SunshineService;

        private void UnWatchInstances(IEnumerable<DuoInstance> instances, bool final = false)
        {
            using (Context.Network.Mutex.Lock()) if (_contexts is not null)
            {
                foreach (var instance in instances.ToArray())
                {
                    if (_contexts.Remove(instance, out var ctx))
                    {
                        UnregisterWatch(instance, ctx.Watch);

                        ctx.Dispose();
                    }
                }

                if (final)
                {
                    _contexts = null; // better to do this inside the lock
                }
            }
        }

        private void Monitor_TrackingStopped(object? sender, InspectableEventArgs<DuoInstance> args)
        {
            UnWatchInstances([args.Inspectable]);
        }

        private void Detach()
        {
            LocalHostContext.Watch?.InspectionFilter -= IsNotSunshineServiceWatch;

            monitor.TrackingStarted -= Monitor_TrackingStarted;
            monitor.TrackingStopped -= Monitor_TrackingStopped;
        }

        async Task INetworkService.Shutdown(NetworkShutdownReason reason)
        {
            if (reason != NetworkShutdownReason.InterfaceDisconnected)
            {
                Logger.LogDebug("NetworkMonitor '{Name}' is disconnected, stop watching instances.", Context.Name);
            }

            UnWatchInstances(_contexts!.Keys, true);

            Detach();
        }
    }
}
