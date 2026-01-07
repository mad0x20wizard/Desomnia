using Autofac;
using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Power.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Net;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    public class DynamicNetworkObserver(ModuleConfig module) : BackgroundService, IIEnumerable<NetworkMonitor>
    {
        static readonly TimeSpan RESUME_GRACE_PERIOD = TimeSpan.FromSeconds(5);

        public required ILogger<DynamicNetworkObserver> Logger { private get; init; }

        public required IPowerManager Power { private get; init; }

        public required Func<NetworkMonitorConfig, NetworkInterface, Owned<NetworkContext>> CreateContext { private get; init; }

        public event EventHandler<NetworkMonitor>? MonitoringStarted;
        public event EventHandler<NetworkMonitor>? MonitoringStopped;

        readonly IList<Owned<NetworkContext>> _contexts = [];

        private readonly AsyncLock _mutex = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogDebug("Start monitoring networks...");

            await ConfigureNetworkMonitors();

            Power.Suspended += PowerManager_Suspended;
            Power.ResumeSuspended += PowerManager_ResumeSuspended;

            //NetworkChange.NetworkAvailabilityChanged += ConfigureNetworkMonitors;
            NetworkChange.NetworkAddressChanged += RespondToNetworkChange;
            try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); } catch (TaskCanceledException) { }
            NetworkChange.NetworkAddressChanged -= RespondToNetworkChange;
            //NetworkChange.NetworkAvailabilityChanged -= ConfigureNetworkMonitors;

            Power.ResumeSuspended -= PowerManager_ResumeSuspended;
            Power.Suspended -= PowerManager_Suspended;

            await UnconfigureNetworkMonitors();

            Logger.LogDebug("Stopped monitoring networks.");
        }

        private async void RespondToNetworkChange(object? sender = null, EventArgs? args = null)
        {
            using (await _mutex.LockAsync()) // process only one change at a time
            {
                await ConfigureNetworkMonitors();
            }
        }

        private async Task ConfigureNetworkMonitors()
        {
            var orphaned = _contexts.ToList();

            foreach (var @interface in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var config in module.NetworkMonitor.Where(@interface.Matches))
                {
                    foreach (var context in _contexts.Where(c => c.Value.Interface.Id == @interface.Id))
                    {
                        context.Value.Interface = @interface; // Update Address config

                        orphaned.Remove(context);

                        goto next;
                    }

                    await StartupContext(config, @interface);

                    goto next;
                }

                next: continue; // allow only one config match per interface
            }

            foreach (var context in orphaned.Where(c => !c.Value.IsSuspended))
            {
                await ShutdownContext(context);
            }
        }

        internal async Task ReconfigureNetworkMonitors()
        {
            using (await _mutex.LockAsync()) // process only one change at a time
            {
                await UnconfigureNetworkMonitors();

                await ConfigureNetworkMonitors();
            }
        }

        private async Task StartupContext(NetworkMonitorConfig config, NetworkInterface @interface)
        {
            var owned = CreateContext(config, @interface); var context = owned.Value;

            context.Device.StartCapture();

            await context.DiscoverRouters();
            await context.DiscoverHosts();
            await context.DiscoverHostRanges();

            context.Monitor.StartMonitoring();

            MonitoringStarted?.Invoke(this, context.Monitor);

            _contexts.Add(owned);
        }

        private async Task ShutdownContext(Owned<NetworkContext> context)
        {
            if (_contexts.Remove(context))
            {
                context.Value.Monitor.StopMonitoring();

                MonitoringStopped?.Invoke(this, context.Value.Monitor);

                context.Value.Device.StopCapture();

                context.Dispose();
            }
        }

        private async Task UnconfigureNetworkMonitors()
        {
            foreach (var context in _contexts.ToArray())
            {
                await ShutdownContext(context);
            }
        }

        IEnumerator<NetworkMonitor> IEnumerable<NetworkMonitor>.GetEnumerator()
        {
            return _contexts.Select(c => c.Value.Monitor).GetEnumerator();
        }

        #region Power Management
        private void PowerManager_Suspended(object? sender, EventArgs e)
        {
            NetworkChange.NetworkAddressChanged -= RespondToNetworkChange;

            Logger.LogDebug("System is suspending, pausing to monitor networks...");

            foreach (var context in _contexts)
            {
                context.Value.IsSuspended = true;
            }
        }

        private async void PowerManager_ResumeSuspended(object? sender, EventArgs e)
        {
            NetworkChange.NetworkAddressChanged += RespondToNetworkChange;

            await Task.Delay(RESUME_GRACE_PERIOD);

            foreach (var context in _contexts)
            {
                context.Value.IsSuspended = false;
            }

            await ConfigureNetworkMonitors(); // now we check if all networks came back up
        }
        #endregion
    }

    file static class NetworkInterfaceMatcher
    {
        internal static bool Matches(this NetworkInterface @interface, NetworkMonitorConfig config)
        {
            if (@interface.OperationalStatus != OperationalStatus.Up)
                return false;

            if (config.Interface != null || config.Network != null)
            {
                if (config.Interface    is string name          && !@interface.MatchesByName(name))
                    return false;

                if (config.Network      is IPNetwork network    && !@interface.MatchesByNetwork(network))
                    return false;

                return true;
            }
            else
            {
                return @interface.GetIPProperties().GatewayAddresses.Count > 0; // if config is not more specific, match interfaces with a gateway
            }
        }

        private static bool MatchesByName(this NetworkInterface @interface, string name)
        {
            if (@interface.Id.Contains(name))
                return true;
            if (@interface.Name.Equals(name))
                return true;

            return false;
        }

        private static bool MatchesByNetwork(this NetworkInterface @interface, IPNetwork network)
        {
            foreach (var unicast in @interface.GetIPProperties().UnicastAddresses)
            {
                if (network.Contains(unicast.Address))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
