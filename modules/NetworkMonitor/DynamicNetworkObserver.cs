using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Interface;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Guard;
using MadWizard.Desomnia.Power.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network
{
    /// <summary>
    /// Owns network contexts and serializes their lifecycle. Interface settings are reconciled
    /// by NetworkInterfaceMonitor before selecting contexts from actual interface state.
    /// </summary>
    public class DynamicNetworkObserver : IHostedService, IPowerTransitionGuard, IIEnumerable<NetworkMonitor>
    {
        static readonly TimeSpan RESUME_GRACE_PERIOD = TimeSpan.FromSeconds(5);

        public required ILogger<DynamicNetworkObserver> Logger { private get; init; }

        public required IPowerManager Power { private get; init; }

        public required NetworkConfigSelector Selector { private get; init; }
        public required NetworkInterfaceMonitor Monitor { private get; init; }

        public required Func<NetworkMonitorConfig, INetworkInterface, Owned<NetworkContext>> CreateContext { private get; init; }

        public event EventHandler<NetworkMonitor>? MonitoringStarted;
        public event EventHandler<NetworkMonitor>? MonitoringStopped;

        readonly IList<Owned<NetworkContext>> _contexts = [];

        private readonly AsyncLock _mutex = new();

        /// <summary>Set under the mutex by the teardown round: a configuration round already
        /// queued on the mutex when the observer stops must find a dead observer — teardown
        /// asserts nothing, and nothing may start monitors or assert intents after it.</summary>
        private bool _stopped;
        private bool _suspended;
        private bool _resumeRequested;
        private long _resumeAfterRevision;

        async Task IHostedService.StartAsync(CancellationToken token)
        {
            Logger.LogDebug("Start monitoring networks...");

            Monitor.Changed += RespondToNetworkChange;

            try
            {
                // No observer lock here: the initial publication awaits our event handler.
                await Monitor.StartAsync(token);

                Power.Suspended += PowerManager_Suspended;
                Power.ResumeSuspended += PowerManager_ResumeSuspended;
            }
            catch
            {
                Monitor.Changed -= RespondToNetworkChange;
                await Monitor.StopAsync();
                throw;
            }
        }

        async Task RespondToNetworkChange(object? sender, EventArgs args)
        {
            try
            {
                using (await _mutex.LockAsync()) // process only one change at a time
                {
                    if (_stopped) return;
                    if (_resumeRequested && Monitor.Revision > _resumeAfterRevision) await ResumeNetworkContexts();
                    await ConfigureNetworkMonitors();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not update network monitoring");
            }
        }

        async Task IHostedService.StopAsync(CancellationToken token)
        {
            Power.ResumeSuspended -= PowerManager_ResumeSuspended;
            Power.Suspended -= PowerManager_Suspended;

            Monitor.Changed -= RespondToNetworkChange;

            // Drain outside the mutex: an in-flight publication may be waiting to enter it.
            await Monitor.StopAsync(token);

            using (await _mutex.LockAsync(token)) // serialize against in-flight network changes
            {
                _stopped = true;

                foreach (var context in _contexts.ToArray())
                {
                    await ShutdownContext(context, NetworkShutdownReason.ApplicationShutdown);
                }

                // teardown asserts NO intents — the flap-free rebuild design: a successor
                // observer's first round (or, at process exit, the manager's dispose
                // self-heal) settles what the configuration then wants
            }

            Logger.LogDebug("Stopped monitoring networks.");
        }

        #region Configuration loop
        /// <summary>
        /// Consumes the published interfaces under the lifecycle mutex. Protected contexts
        /// retain their lifetime; interface enforcement is owned independently by the monitor.
        /// </summary>
        private async Task ConfigureNetworkMonitors()
        {
            if (_stopped || _suspended) return;

            var orphaned = _contexts.ToList();

            foreach (var @interface in Monitor.GetAvailableInterfaces())
            {
                foreach (var config in Selector.ByInterface(@interface))
                {
                    // The manager preserves handle identity across interface changes.
                    if (_contexts.FirstOrDefault(c => c.Value.Interface == @interface && c.Value.Config == config) is { } existing)
                    {
                        orphaned.Remove(existing); break;
                    }

                    try
                    {
                        await StartupContext(config, @interface); break; // one monitor per interface; later configs are fallbacks
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to startup monitoring context for '{@interface.Name}'"
                            + (config.Name is string label ? $" ['{label}']" : ""));
                    }
                }
            }

            foreach (var context in orphaned.Where(c => !c.Value.IsProtected))
            {
                // An unreadable administrative state is not evidence of disconnection.
                // Keep an existing operational context until a later round resolves it.
                if (Monitor.IsUnresolved(context.Value.Interface)
                    && context.Value.Interface.Status == OperationalStatus.Up)
                    continue;

                await ShutdownContext(context, NetworkShutdownReason.InterfaceDisconnected);
            }
        }
        #endregion

        #region Context startup / shutdown
        private async Task StartupContext(NetworkMonitorConfig config, INetworkInterface @interface)
        {
            var owned = CreateContext(config, @interface); var context = owned.Value;

            try
            {
                context.Device.StartCapture();

                await context.DiscoverHosts();
                await context.DiscoverHostRanges();

                // Monitoring (packet fan-out to services) starts before router discovery so that
                // IRouterDiscovery implementations can use the mDNS browser — which only receives
                // once HandlePacket is wired. Routers are still created before CreateDynamicFilterHosts
                // and DiscoverAddresses, so router-referencing filter rules resolve and the router's
                // (and its VPN clients') addresses are discovered in the normal pass.
                await context.Monitor.StartMonitoring();

                await context.DiscoverRouters();

                context.CreateDynamicFilterHosts();

                await context.DiscoverAddresses();

                await context.DiscoverServices();

                await context.Monitor.StartWatch();

                Logger.LogDebug("Monitoring of '" + context.Monitor.Name + "' has been started");

                _contexts.Add(owned);
            }
            catch (Exception)
            {
                owned.Dispose();

                throw;
            }

            await context.Monitor.TriggerAfterStartup(); // run AfterStartup() triggers

            // AFTER _contexts.Add and OUTSIDE the try (spec §7.2): a throwing subscriber
            // must never tear down the freshly started context, and the observer's own
            // enumeration already includes the monitor when subscribers run
            MonitoringStarted?.Invoke(this, context.Monitor);
        }

        private async Task ShutdownContext(Owned<NetworkContext> context, NetworkShutdownReason reason)
        {
            if (_contexts.Remove(context))
            {
                try
                {
                    await context.Value.Monitor.StopMonitoring(reason);
                }
                catch (Exception ex)
                {
                    // teardown on a dead interface may throw (handoff/WoL, SharpPcap) —
                    // the hand-off pairing and the scope disposal must happen regardless,
                    // or the monitor lingers in the inspection roster forever
                    Logger.LogError(ex, $"Failed to stop monitoring of '{context.Value.Monitor.Name}' cleanly");
                }
                finally
                {
                    MonitoringStopped?.Invoke(this, context.Value.Monitor);

                    Logger.LogDebug("Monitoring of '" + context.Value.Monitor.Name + "' has been stopped");

                    context.Dispose();
                }
            }
        }
        #endregion

        #region Power events
        async Task IPowerTransitionGuard.BeforeTransition(PowerTransition transition)
        {
            using (await _mutex.LockAsync())
            {
                switch (transition)
                {
                    case PowerTransition.Suspend when !(_stopped || _suspended):
                        try
                        {
                            foreach (var context in _contexts)
                            {
                                using var protection = context.Value.Protect();

                                await context.Value.Monitor.BeforeSuspend();
                            }

                            break;
                        }
                        finally
                        {
                            // Interface events are hints. Reconcile the latest snapshot after all work,
                            // including a failed preparation, without replaying transient Down states.
                            Monitor.RequestReconciliation();
                        }
                }
            }
        }

        private async void PowerManager_Suspended(object? sender, EventArgs args)
        {
            try
            {
                using (await _mutex.LockAsync())
                {
                    if (_stopped || _suspended) return; else _suspended = true;

                    Logger.LogDebug("System is suspending, pausing network monitoring...");

                    foreach (var context in _contexts)
                    {
                        try
                        {
                            await context.Value.Suspend();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Could not suspend {Network}", context.Value.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not pause network monitoring for system suspend");
            }
        }

        private async void PowerManager_ResumeSuspended(object? sender, EventArgs args)
        {
            try
            {
                using (await _mutex.LockAsync())
                {
                    if (_stopped || !_suspended) return;

                    // Keep power notifications, network changes and teardown in lifecycle order.
                    await Task.Delay(RESUME_GRACE_PERIOD);

                    // Resume only from a fresh publication. Awaiting a monitor round here
                    // would deadlock against its subscriber waiting for this mutex.
                    _resumeRequested = true;
                    _resumeAfterRevision = Monitor.Revision;
                    Monitor.RequestReconciliation();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not resume network monitoring");
            }
        }
        private async Task ResumeNetworkContexts()
        {
            var interfaces = Monitor.GetAvailableInterfaces();
            foreach (var context in _contexts.Select(owned => owned.Value))
            {
                try
                {
                    if (interfaces.Contains(context.Interface)
                        && context.Interface.Status == OperationalStatus.Up)
                        await context.Resume();
                    else
                        context.EndSuspension();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not resume {Network}", context.Name);
                }
            }
            _resumeRequested = false;
            _suspended = false;
        }
        #endregion

        IEnumerator<NetworkMonitor> IEnumerable<NetworkMonitor>.GetEnumerator()
        {
            return _contexts.Select(c => c.Value.Monitor).GetEnumerator();
        }
    }
}
