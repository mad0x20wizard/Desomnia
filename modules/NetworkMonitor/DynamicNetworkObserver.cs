using Autofac.Features.OwnedInstances;
using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Bridges;
using MadWizard.Desomnia.Network.Configuration;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Manager;
using MadWizard.Desomnia.Power.Manager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System.Net.NetworkInformation;

// one configured network: its matcher, plus the matchers for its <NetworkInterfaceBlock> children
using ConfiguredNetwork = (MadWizard.Desomnia.Network.Configuration.NetworkMonitorConfig Config,
    MadWizard.Desomnia.Network.Bridges.InterfaceMatcher Matcher,
    System.Collections.Generic.IReadOnlyList<(MadWizard.Desomnia.Network.Bridges.InterfaceMatcher Matcher, bool Force)> Blocks);

// a config about to run on a concrete interface, suppressing the monitors of others
using NetworkBlocker = (MadWizard.Desomnia.Network.Configuration.NetworkMonitorConfig Config,
    MadWizard.Desomnia.Network.Manager.INetworkInterface Interface);

namespace MadWizard.Desomnia.Network
{
    /// <summary>
    /// Runs the configured network monitors on the interfaces they match — and is the sole
    /// arbiter of the desired interface-block state: every configuration round asserts the
    /// complete set (the environment's root-level blocks outranking, plus the blocks of every
    /// live monitor) on the <see cref="INetworkInterfaceManager"/> via intent, which
    /// reconciles the OS. Teardown asserts nothing — a rebuild's successor round (or, at
    /// process exit, the manager's dispose self-heal) settles reality, so a configuration
    /// rebuild never flaps the interfaces.
    /// </summary>
    public class DynamicNetworkObserver(
        IEnumerable<NetworkMonitorConfig> configs,
        IEnumerable<NetworkInterfaceBlockInfo> blocks) 
            : BackgroundService, IIEnumerable<NetworkMonitor>
    {
        static readonly TimeSpan RESUME_GRACE_PERIOD = TimeSpan.FromSeconds(5);

        public required ILogger<DynamicNetworkObserver> Logger { private get; init; }

        public required IPowerManager Power { private get; init; }
        public required INetworkInterfaceManager Manager { private get; init; }

        /// <summary>
        /// Resolved rather than constructed, so that a platform host registering its own
        /// <see cref="InterfaceMatcher"/> takes over the matching here as well.
        /// </summary>
        public required Func<InterfaceMatcher> CreateMatcher { private get; init; }
        public required Func<NetworkMonitorConfig, INetworkInterface, Owned<NetworkContext>> CreateContext { private get; init; }

        public event EventHandler<NetworkMonitor>? MonitoringStarted;
        public event EventHandler<NetworkMonitor>? MonitoringStopped;

        readonly IList<Owned<NetworkContext>> _contexts = [];

        /// <summary>One matcher per configured network, in configuration order.</summary>
        readonly List<ConfiguredNetwork> _matchers = [];

        /// <summary>The root-level (environment-scoped) blocks — they outrank the monitors.</summary>
        readonly List<(InterfaceMatcher Matcher, bool Force)> _envBlocks = [];

        private readonly AsyncLock _mutex = new();

        /// <summary>Set under the mutex by the teardown round: a configuration round already
        /// queued on the mutex when the observer stops must find a dead observer — teardown
        /// asserts nothing, and nothing may start monitors or assert intents after it.</summary>
        private bool _stopped;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!configs.Any() && !blocks.Any())
                return; // nothing wants interface management — never create the manager

            Logger.LogDebug("Start monitoring networks...");

            CreateInterfaceMatchers();

            // Attached BEFORE the power events: a suspend delivered mid-startup then detaches
            // a handler that is actually there, so the resume's re-attach stays balanced —
            // the +=/-= choreography spread over three handlers must never go negative, or a
            // leftover subscription on the persistent manager roots this rebuild's graph.
            Manager.Changed += RespondToNetworkChange;

            using (await _mutex.LockAsync())
                await ConfigureNetworkMonitors();

            Power.Suspended += PowerManager_Suspended;
            Power.ResumeSuspended += PowerManager_ResumeSuspended;

            try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); } catch (TaskCanceledException) { }

            Power.ResumeSuspended -= PowerManager_ResumeSuspended;
            Power.Suspended -= PowerManager_Suspended;

            await UnconfigureNetworkMonitors(); // stamps the observer stopped and detaches manager.Changed

            Logger.LogDebug("Stopped monitoring networks.");
        }

        private async void RespondToNetworkChange(object? sender = null, EventArgs? args = null)
        {
            using (await _mutex.LockAsync()) // process only one change at a time
            {
                await ConfigureNetworkMonitors();
            }
        }

        /// <summary>
        /// Builds the matchers once, before the first pass — the configuration they are derived
        /// from does not change while the application runs.
        /// </summary>
        private void CreateInterfaceMatchers()
        {
            foreach (var config in configs)
            {
                var matcher = CreateMatcher()
                    .WithType(NetworkInterfaceType.Ethernet, NetworkInterfaceType.Wireless80211)
                    .WithStatus(OperationalStatus.Up)
                    .WithInterface(config.Interface)
                    .WithNetwork(config.Network)
                    .WithSSID(config.SSID)
                    // without a criterion of its own, a network is whichever one carries the default route
                    .WithGateway(config.Interface is null && config.Network is null && config.SSID is null);

                // the <NetworkInterfaceBlock> children, mirrored here: an interface this config
                // is about to block never gets a monitor in the first place (see ResolveBlockedInterfaces)
                var configBlocks = config.NetworkInterfaceBlock
                    .Select(block => (CreateBlockMatcher(block), block.Force))
                    .ToList();

                _matchers.Add((config, matcher, configBlocks));
            }

            foreach (var block in blocks)
            {
                _envBlocks.Add((CreateBlockMatcher(block), block.Force));
            }
        }

        private InterfaceMatcher CreateBlockMatcher(NetworkInterfaceBlockInfo block)
        {
            if (block.Interface is not string pattern)
                throw new ConfigurationValueException("A <NetworkInterfaceBlock> needs an interface attribute — without one it would block every interface.");

            return CreateMatcher().WithInterface(pattern);
        }

        /// <summary>
        /// One complete configuration round: plan which config runs on which interface, stop
        /// monitors on interfaces about to be blocked, start the missing monitors (retrying
        /// while blockers turn out defunct), and finally assert the desired blocked set.
        /// The caller MUST hold <see cref="_mutex"/>: the round reads and mutates
        /// <see cref="_contexts"/> across awaits and asserts the complete intent set on the
        /// manager — two interleaved rounds would double-start monitors for the same
        /// interface and fight over the desired state. Every caller (startup, network
        /// changes, the resume handler) wraps its call; <see cref="UnconfigureNetworkMonitors"/>
        /// is the terminal round and takes the mutex itself.
        /// </summary>
        private async Task ConfigureNetworkMonitors()
        {
            if (_stopped)
                return; // queued behind teardown — the observer is dead, nothing may be asserted

            // ---- Plan first, start later: which config would run on which interface? ----

            // Matched against everything the manager knows — including a disabled interface
            // (present, reads as Down) and one it only still holds through its disable intent —
            // so a standing block keeps standing.
            var known = Manager.ToList();

            var envBlocked = ResolveEnvironmentBlocks(known, _envBlocks);

            var candidates = SelectCandidates(Manager, _matchers, envBlocked);

            // The environment's blocks outrank the monitors: a monitor running on an interface
            // the environment blocks loses its monitor FIRST, while the adapter is still up —
            // capture must be stopped before the disable lands (see ShutdownContext).
            foreach (var context in _contexts.Where(c => !c.Value.IsSuspended && envBlocked.ContainsKey(c.Value.Interface)).ToArray())
            {
                Logger.LogInformation($"Stopping monitoring of '{context.Value.Monitor.Name}', " +
                    "its interface is blocked by the environment.");

                await ShutdownContext(context, NetworkShutdownReason.InterfaceShutdown);
            }

            HashSet<NetworkBlocker> defunct = [];

            var blocked = ResolveBlockedInterfaces(_matchers, candidates, defunct, Logger);

            // An interface about to be blocked loses its monitor FIRST, while the adapter is
            // still up: the block disables the interface once the desired set is asserted, and
            // teardown would happen on a dead device (see ShutdownContext).
            foreach (var context in _contexts.Where(c => !c.Value.IsSuspended && blocked.ContainsKey(c.Value.Interface)).ToArray())
            {
                var blocker = blocked[context.Value.Interface];

                Logger.LogInformation($"Stopping monitoring of '{context.Value.Monitor.Name}', " +
                    $"its interface is blocked in favor of '{blocker.Interface.Name}'.");

                await ShutdownContext(context, NetworkShutdownReason.InterfaceShutdown);
            }

            var orphaned = _contexts.ToList();

            while (true) // until every block is backed by a running monitor
            {
                foreach (var (@interface, matching) in candidates)
                {
                    // A running context always wins: it is never torn down by a (re-)planned
                    // block - the shutdown steps above are the only place a block ends a
                    // monitor, so later rounds cannot kill what this pass started. The handle
                    // is stable, so reference equality identifies the context's interface.
                    foreach (var context in _contexts.Where(c => c.Value.Interface == @interface))
                    {
                        orphaned.Remove(context);

                        goto next;
                    }

                    if (blocked.TryGetValue(@interface, out var blocker))
                    {
                        Logger.LogDebug($"Not monitoring '{@interface.Name}', blocked in favor of '{blocker.Interface.Name}'.");

                        continue;
                    }

                    foreach (var (config, _, _) in matching)
                    {
                        try
                        {
                            await StartupContext(config, @interface);

                            goto next;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, $"Failed to startup monitoring context for '{@interface.Name}'"
                                + (config.Name is string label ? $" ['{label}']" : ""));
                        }
                    }

                    next: continue; // allow only one config match per interface
                }

                // A blocker that did not end up with a running monitor - its startup failed, or
                // an earlier config blocked it in turn - blocks nothing after all: lift its
                // blocks, so the interfaces it suppressed get their monitor in another round.
                var lifted = blocked.Values.Distinct().Where(blocker => !_contexts.Any(c =>
                    c.Value.Config == blocker.Config && c.Value.Interface == blocker.Interface)).ToList();

                if (lifted.Count == 0)
                    break;

                defunct.UnionWith(lifted);

                blocked = ResolveBlockedInterfaces(_matchers, candidates, defunct, Logger);
            }

            foreach (var context in orphaned.Where(c => !c.Value.IsSuspended))
            {
                await ShutdownContext(context, NetworkShutdownReason.InterfaceDisconnected);
            }

            ApplyInterfaceBlocks(known, envBlocked);
        }

        /// <summary>
        /// Asserts the complete desired blocked set on the manager. Every handle the manager
        /// knows that is not in the set is declared enabled — releasing happens implicitly
        /// for interfaces that left the set, stale intents of a predecessor included; the
        /// manager reconciles and restores only what it actually took away. Runs after every
        /// shutdown/startup step of the round, so a monitor's capture is always stopped
        /// before its interface's disable lands.
        /// </summary>
        private void ApplyInterfaceBlocks(IReadOnlyList<INetworkInterface> known, IReadOnlyDictionary<INetworkInterface, bool> envBlocked)
        {
            var monitors = _contexts.Select(context => (context.Value.Interface,
                _matchers.First(entry => entry.Config == context.Value.Config).Blocks)).ToList();

            var desired = ComputeDesiredBlocks(known, envBlocked, monitors, Logger);

            foreach (var @interface in known)
            {
                if (desired.TryGetValue(@interface, out bool force))
                {
                    @interface.EnforceDisabled = force;
                    @interface.ShouldBeDisabled = true;
                }
                else
                {
                    @interface.ShouldBeDisabled = false;
                    @interface.EnforceDisabled = false;
                }
            }
        }

        /// <summary>
        /// Which interfaces the root-level (environment-scoped) blocks designate: interface
        /// to force flag, the flag OR-ed over the matching blocks.
        /// </summary>
        internal static Dictionary<INetworkInterface, bool> ResolveEnvironmentBlocks(
            IEnumerable<INetworkInterface> known,
            IEnumerable<(InterfaceMatcher Matcher, bool Force)> envBlocks)
        {
            Dictionary<INetworkInterface, bool> blocked = [];

            foreach (var @interface in known)
            {
                foreach (var (matcher, force) in envBlocks)
                {
                    if (matcher.Matches(@interface))
                    {
                        blocked[@interface] = blocked.GetValueOrDefault(@interface) | force;
                    }
                }
            }

            return blocked;
        }

        /// <summary>
        /// The interfaces a configuration round may start monitors on, with their matching
        /// configs in configuration order. The environment's blocks OUTRANK the monitors:
        /// an environment-blocked interface never gets a monitor — it does not even become
        /// a candidate.
        /// </summary>
        internal static List<(INetworkInterface Interface, List<ConfiguredNetwork> Matching)> SelectCandidates(
            IEnumerable<INetworkInterface> interfaces,
            IReadOnlyList<ConfiguredNetwork> matchers,
            IReadOnlyDictionary<INetworkInterface, bool> envBlocked)
        {
            List<(INetworkInterface, List<ConfiguredNetwork>)> candidates = [];

            foreach (var @interface in interfaces)
            {
                if (envBlocked.ContainsKey(@interface))
                    continue;

                var matching = matchers.Where(entry => entry.Matcher.Matches(@interface)).ToList();

                if (matching.Count > 0)
                    candidates.Add((@interface, matching));
            }

            return candidates;
        }

        /// <summary>
        /// The complete desired blocked set for one round: the environment's blocks united
        /// with the blocks of every LIVE monitor, the force flag OR-ed over every matching
        /// block. No live monitor's interface ever enters the set — a running context always
        /// wins: the defunct-retry re-plan can legitimately end with a live monitor on an
        /// interface another live monitor's blocks match, and disabling it would kill its
        /// capture under inhibition (the very thing the shutdown-before-block ordering
        /// exists to prevent). The environment's blocks need no such exemption: an
        /// environment-blocked interface lost its monitor before the round started planning.
        /// </summary>
        internal static Dictionary<INetworkInterface, bool> ComputeDesiredBlocks(
            IEnumerable<INetworkInterface> known,
            IReadOnlyDictionary<INetworkInterface, bool> envBlocked,
            IReadOnlyList<(INetworkInterface Interface, IReadOnlyList<(InterfaceMatcher Matcher, bool Force)> Blocks)> monitors,
            ILogger logger)
        {
            Dictionary<INetworkInterface, bool> desired = [];

            foreach (var @interface in known)
            {
                bool blocked = envBlocked.TryGetValue(@interface, out bool force);

                bool live = monitors.Any(monitor => monitor.Interface == @interface);

                foreach (var (own, monitorBlocks) in monitors)
                {
                    if (@interface == own)
                        continue; // never the blocker's own interface (the planning warned)

                    foreach (var (matcher, blockForce) in monitorBlocks)
                    {
                        if (!matcher.Matches(@interface))
                            continue;

                        if (live)
                        {
                            logger.LogWarning($"Not disabling interface '{@interface.Name}' although a " +
                                $"<NetworkInterfaceBlock> of the monitor on '{own.Name}' matches it — " +
                                "it carries a running monitor itself, and a running monitor always wins.");

                            continue;
                        }

                        blocked = true;
                        force |= blockForce;
                    }
                }

                if (blocked)
                    desired[@interface] = force;
            }

            return desired;
        }

        /// <summary>
        /// Determines which candidate interfaces must not be monitored, because another
        /// candidate's configuration blocks them (NetworkInterfaceBlock). Blockers act in
        /// configuration order rather than interface enumeration order - the latter is
        /// OS-dependent (on macOS the WiFi "en0" enumerates before any wired adapter), the
        /// former is the user's deliberate choice. An interface that is blocked runs nothing,
        /// so it blocks nothing in turn.
        /// </summary>
        /// <param name="defunct">Blockers whose monitor did not come up; their blocks are void.</param>
        internal static Dictionary<INetworkInterface, NetworkBlocker> ResolveBlockedInterfaces(
            IEnumerable<ConfiguredNetwork> matchers,
            List<(INetworkInterface Interface, List<ConfiguredNetwork> Matching)> candidates,
            IReadOnlySet<NetworkBlocker> defunct, ILogger logger)
        {
            Dictionary<INetworkInterface, NetworkBlocker> blocked = [];

            foreach (var entry in matchers) // configuration order
            {
                if (entry.Blocks.Count == 0)
                    continue;

                foreach (var (@interface, matching) in candidates)
                {
                    if (matching[0] != entry)
                        continue; // this config only blocks where it is the one about to run

                    if (blocked.ContainsKey(@interface) || defunct.Contains((entry.Config, @interface)))
                        continue;

                    foreach (var victim in candidates)
                    {
                        if (victim.Interface == @interface)
                        {
                            // never the blocker's own interface
                            if (entry.Blocks.Any(block => block.Matcher.Matches(victim.Interface)))
                            {
                                logger.LogWarning($"The monitored interface '{victim.Interface.Name}' " +
                                    "matches its own <NetworkInterfaceBlock> — ignoring it.");
                            }

                            continue;
                        }

                        if (blocked.ContainsKey(victim.Interface))
                            continue;

                        if (!entry.Blocks.Any(block => block.Matcher.Matches(victim.Interface)))
                            continue;

                        if (victim.Matching[0].Blocks.Any(block => block.Matcher.Matches(@interface)))
                        {
                            logger.LogWarning($"The interfaces '{victim.Interface.Name}' and '{@interface.Name}' " +
                                "block each other; configuration order decides, the earlier one stays.");
                        }

                        blocked[victim.Interface] = (entry.Config, @interface);
                    }
                }
            }

            return blocked;
        }

        #region Startup / Shutdown
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

        /// <summary>
        /// The terminal round — the one round that takes the mutex itself: it stamps the
        /// observer stopped, so a configuration round already queued on the mutex (or an
        /// in-flight resume about to re-attach) finds a dead observer and does nothing.
        /// </summary>
        private async Task UnconfigureNetworkMonitors()
        {
            using (await _mutex.LockAsync()) // serialize against in-flight network changes
            {
                _stopped = true;

                // detached under the mutex, after the stamp: an in-flight resume either
                // re-attached the handler before (this removes it) or sees the stamp and
                // leaves it detached — the persistent manager never keeps a subscription
                // that would root this rebuild's graph
                Manager.Changed -= RespondToNetworkChange;

                foreach (var context in _contexts.ToArray())
                {
                    await ShutdownContext(context, NetworkShutdownReason.ApplicationShutdown);
                }

                // teardown asserts NO intents — the flap-free rebuild design: a successor
                // observer's first round (or, at process exit, the manager's dispose
                // self-heal) settles what the configuration then wants
            }
        }

        IEnumerator<NetworkMonitor> IEnumerable<NetworkMonitor>.GetEnumerator()
        {
            return _contexts.Select(c => c.Value.Monitor).GetEnumerator();
        }

        #region PowerManager events
        private void PowerManager_Suspended(object? sender, EventArgs e)
        {
            Manager.Changed -= RespondToNetworkChange;

            Logger.LogDebug("System is suspending, pausing to monitor networks...");

            using (_mutex.Lock()) // don't race an in-flight network change
            {
                foreach (var context in _contexts)
                {
                    context.Value.Suspend();
                }
            }
        }

        private async void PowerManager_ResumeSuspended(object? sender, EventArgs e)
        {
            using (await _mutex.LockAsync())
            {
                if (_stopped)
                    return; // torn down while suspended — nothing to re-attach, nothing to resume

                Manager.Changed += RespondToNetworkChange;
            }

            await Task.Delay(RESUME_GRACE_PERIOD);

            // one mutex scope over the resume AND the round: an unserialized Configure here
            // could birth a second context for the same interface — and the round asserts
            // intents, which must never interleave with a competing round's plan
            using (await _mutex.LockAsync())
            {
                foreach (var context in _contexts)
                {
                    if (Manager[context.Value.Interface.Identity] is not null)
                        context.Value.Resume();
                    else
                        context.Value.EndSuspension(); // dead — the following pass shuts it down
                }

                await ConfigureNetworkMonitors(); // now we check if all networks came back up
            }
        }
        #endregion
    }
}
