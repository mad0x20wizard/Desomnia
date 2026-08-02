using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Watch;
using MadWizard.Desomnia.Power.Guard;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace MadWizard.Desomnia.Network
{
    public class NetworkMonitor : ResourceMonitor<NetworkHostWatch>, IPowerTransitionGuard
    {
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

        public NetworkHostWatch? this[NetworkHost host] => this.FirstOrDefault(w => w.Host == host);

        public bool IsWatchedBy<T>(EthernetPacket packet) where T : NetworkHostWatch
        {
            return this.OfType<T>().Any(w => w.Host.HasAddress(packet.FindTargetPhysicalAddress(), packet.FindTargetIPAddress()));
        }

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
            Device.EthernetCaptured += HandlePacket;

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

            Device.EthernetCaptured -= HandlePacket;
            Device.StopCapture();

            Disconnected.TriggerEvent();

            Logger.LogDebug($"Monitoring of '{Name}' has been stopped.");
        }
    }
}