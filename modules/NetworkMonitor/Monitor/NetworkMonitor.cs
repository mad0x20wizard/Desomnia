using Autofac.Features.Indexed;
using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Power.Guard;
using MadWizard.Desomnia.Ressource.Events;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MadWizard.Desomnia.Network
{
    public class NetworkMonitor : ResourceMonitor<NetworkHostWatch>, IPowerTransitionGuard
    {
        readonly IIndex<NetworkHost, NetworkHostWatch> _index;

        public required ILogger<NetworkMonitor> Logger { private get; init; }

        public required string Name { get; init; }

        public required WatchOptions Options { get; init; }

        public required NetworkDevice   Device  { internal get; init; }
        public required NetworkSegment  Network { get; init; }
        public required NetworkJanitor  Janitor { private get; init; }

        /// <summary>
        /// The monitored interface — declared as event context so that action handlers
        /// (plugins, URL actions) can act on it for any event triggered on this monitor.
        /// </summary>
        [EventContext]
        public INetworkInterface Interface => Device.Interface;

        public IEnumerable<INetworkService> Services { private get; init; } = [];

        public event EventInvocation? Connected;
        public event EventInvocation? Disconnected;

        public NetworkMonitor()
        {
            _index = new NetworkMonitorIndex(this);
        }

        public NetworkHostWatch? this[NetworkHost host] => _index.TryGetValue(host, out var watch) ? watch : null;

        public override IEnumerable<UsageToken> Inspect(TimeSpan interval)
        {
            using (Network.Mutex.Lock())
            {
                return base.Inspect(interval);
            }
        }

        async Task IPowerTransitionGuard.BeforeTransition(PowerTransition transition)
        {
            if (transition == PowerTransition.Suspend)
            {
                foreach (var service in Services)
                {
                    await service.BeforeSuspend();
                }
            }
        }

        internal async Task StartMonitoring()
        {
            Device.StartCapture();
            Device.PacketCaptured += HandlePacket;

            foreach (var service in Services)
                await service.Startup();

            Janitor.StartSweeping();

            Logger.LogDebug($"Monitoring of '{Name}' has been started.");
        }

        internal async Task StartWatch()
        {
            foreach (var watch in this)
            {
                await watch.StartWatch();
            }
        }

        internal async Task TriggerAfterStartup()
        {
            foreach (var service in Services)
            {
                await service.AfterStartup();
            }

            Connected.TriggerEvent();
        }

        internal void ResumeMonitoring()
        {
            Logger.LogDebug($"Monitoring of '{Name}' will now continue...");

            Device.StartCapture();

            foreach (var service in Services)
                service.Resume();
        }

        private void HandlePacket(object? sender, EthernetPacket packet)
        {
            using (Network.Mutex.Lock())
            {
                foreach (var service in Services)
                {
                    service.ProcessPacket(packet);
                }
            }
        }

        internal void SuspendMonitoring()
        {
            foreach (var service in Services.Reverse())
                service.Suspend();

            Device.StopCapture();

            Logger.LogDebug($"Monitoring of '{Name}' has been paused.");
        }

        internal async Task StopMonitoring(NetworkShutdownReason reason)
        {
            Janitor.StopSweeping();

            // per-participant teardown is guarded: one failing watch (handoff/WoL on a
            // dead interface) must not skip the remaining watches or the services'
            // Shutdown seam — plugins orphan their event handles there, and a skipped
            // orphaning wedges those events on the disposed monitor
            foreach (var watch in this)
            {
                var gracefully = reason == NetworkShutdownReason.ApplicationShutdown
                    || reason == NetworkShutdownReason.InterfaceShutdown;

                try
                {
                    await watch.StopWatch(gracefully);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Failed to stop {watch} cleanly");
                }
            }

            foreach (var service in Services.Reverse())
            {
                try
                {
                    await service.Shutdown(reason);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Failed to shut down {service} cleanly");
                }
            }

            Device.PacketCaptured -= HandlePacket;
            Device.StopCapture();

            Disconnected.TriggerEvent();

            Logger.LogDebug($"Monitoring of '{Name}' has been stopped.");
        }
    }
}

file class NetworkMonitorIndex : IIndex<NetworkHost, NetworkHostWatch>
{
    private readonly ConcurrentDictionary<NetworkHost, NetworkHostWatch> _watchesByHost = [];

    public NetworkMonitorIndex(NetworkMonitor monitor)
    {
        monitor.TrackingStarted += NetworkMonitor_TrackingStarted;
        monitor.TrackingStopped += NetworkMonitor_TrackingStopped;
    }

    private void NetworkMonitor_TrackingStarted(object? sender, InspectableEventArgs<NetworkHostWatch> args)
    {
        _watchesByHost[args.Inspectable.Host] = args.Inspectable;
    }

    private void NetworkMonitor_TrackingStopped(object? sender, InspectableEventArgs<NetworkHostWatch> args)
    {
        if (_watchesByHost.TryGetValue(args.Inspectable.Host, out var current)
            && ReferenceEquals(current, args.Inspectable))
        {
            _watchesByHost.TryRemove(args.Inspectable.Host, out _);
        }
    }

    public NetworkHostWatch this[NetworkHost host] => _watchesByHost[host];

    bool IIndex<NetworkHost, NetworkHostWatch>.TryGetValue(NetworkHost host, [MaybeNullWhen(false)] out NetworkHostWatch watch)
    {
        return _watchesByHost.TryGetValue(host, out watch);
    }
}