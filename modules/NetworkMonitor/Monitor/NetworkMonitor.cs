using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using MadWizard.Desomnia.Network.Neighborhood.Events;
using MadWizard.Desomnia.Network.Services;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace MadWizard.Desomnia.Network
{
    public class NetworkMonitor : ResourceMonitor<NetworkHostWatch>
    {
        public required ILogger<NetworkMonitor> Logger { private get; init; }

        public string Name { get => field ?? Device.Interface.Name; internal set; }

        public required WatchOptions Options { get; init; }

        public required NetworkDevice   Device  { private get; init; }
        public required NetworkSegment  Network { private get; init; }
        public required NetworkJanitor  Janitor { private get; init; }

        public IEnumerable<INetworkService> Services { private get; init; } = [];

        public event EventInvocation? Connected;
        public event EventInvocation? Disconnected;

        public NetworkHostWatch? this[NetworkHost host] => this.Where(w => w.Host == host).FirstOrDefault();

        public NetworkMonitor(string? name)
        {
            Name = name!;
        }

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

        internal void StartMonitoring()
        {
            Device.EthernetCaptured += HandlePacket;

            foreach (var service in Services)
            {
                service.Startup();
            }

            TriggerEvent(nameof(Connected));

            Network.HostRemoved += Network_HostRemoved;

            Janitor.StartSweeping();
        }

        internal void ResumeMonitoring()
        {
            Logger.LogDebug($"Monitoring of '{Name}' will now continue...");

            Device.StartCapture();

            foreach (var service in Services)
            {
                service.Resume();
            }
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
            foreach (var service in Services)
            {
                service.Suspend();
            }

            Device.StopCapture();

            Logger.LogDebug($"Monitoring of '{Name}' has been paused.");
        }

        internal void StopMonitoring()
        {
            Janitor.StopSweeping();

            Network.HostRemoved -= Network_HostRemoved;

            Device.EthernetCaptured -= HandlePacket;

            foreach (var service in Services)
            {
                service.Shutdown();
            }

            TriggerEvent(nameof(Disconnected));
        }

        #region Host Events
        private void Network_HostRemoved(object? sender, NetworkHostEventArgs e)
        {
            if (this[e.Host] is NetworkHostWatch watch)
            {
                StopTracking(watch);
            }
        }
        #endregion
    }
}