using Autofac;
using MadWizard.Desomnia.Display.Configuration;
using MadWizard.Desomnia.Display.Manager;
using MadWizard.Desomnia.Display.Watch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Display
{
    /// <summary>
    /// Watches the configured displays AND asserts the desired soft-disconnect state for
    /// every display the manager knows: watched displays get their effective configured
    /// <c>disabled</c> value, all others are released to false. Runs even without a
    /// <c>&lt;DisplayMonitor&gt;</c> configuration — but only when a previous configuration
    /// already created the manager (see <see cref="CreationTracker{TService}"/>): that
    /// config-less pass sweeps stale intents to false and tracks nothing, so a dropped
    /// configuration never leaves a display held off.
    /// </summary>
    public class DisplayMonitor(IDisplayManager manager, DisplayMonitorConfig? config) : ResourceMonitor<DisplayWatch>, IHostedService
    {
        public required ILogger<DisplayMonitor> Logger { get; set; }

        public required ILifetimeScope Scope { private get; init; }

        private readonly Lock _lock = new();

        private readonly Dictionary<DisplayWatchExternal, ILifetimeScope> _watchScopes = [];

        // disconnects are debounced: hot-plug re-negotiation pulses (mode changes, TV/AVR
        // power transitions) must not tear down the watch or flap the demand state
        private readonly Dictionary<DisplayWatchExternal, CancellationTokenSource> _pendingDisconnects = [];

        async Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            if (config is not null)
            {
                Event(nameof(Idle)).AddAction(config.OnIdle);
                Event(nameof(Demand)).AddAction(config.OnDemand);
            }

            // subscribed BEFORE the initial enumeration: the persistent manager raises its
            // events on its own notification thread, so a display (dis)connecting mid-startup
            // would otherwise fall between snapshot and subscription — untracked and unswept
            // until it physically re-cycles. A display delivered through BOTH paths is
            // absorbed: TrackDisplay skips what is already tracked (or gone again), and
            // AssertDisabled recomputes under the lock, so the last assertion converges.
            manager.DisplayConnected += Manager_DisplayConnected;
            manager.DisplayDisconnected += Manager_DisplayDisconnected;

            if (config is not null)
            {
                if (config.DisplayBuiltIn is DisplayBuiltInDescriptor builtInDesc)
                {
                    if (manager.BuiltIn is IDisplayBuiltIn display)
                        TrackBuiltInDisplay(display, builtInDesc);
                    else
                    {
                        Logger.LogWarning("No built-in display present — ignoring <DisplayBuiltIn> configuration.");
                    }
                }

                foreach (IDisplayExternal display in manager.OfType<IDisplayExternal>())
                    TrackDisplay(display);
            }

            // the first pass of the desired-state sweep, after the watches exist: watched
            // displays receive their effective value, everything else is released to false —
            // including intents a dropped configuration left behind
            foreach (IDisplay display in manager)
                AssertDisabled(display);

            Logger.LogDebug("Startup complete");
        }

        #region DisplayManager events
        private void Manager_DisplayConnected(object? sender, IDisplayExternal display)
        {
            if (config is not null)
                TrackDisplay(display, connect: true);

            AssertDisabled(display); // after the watch exists, so it sees the effective value
        }

        private void Manager_DisplayDisconnected(object? sender, IDisplayExternal display)
        {
            lock (_lock)
            {
                if (FindWatch(display) is not DisplayWatchExternal watch || _pendingDisconnects.ContainsKey(watch))
                    return;

                var cts = new CancellationTokenSource();

                _pendingDisconnects[watch] = cts;

                _ = DebounceDisconnect(watch, cts.Token);
            }
        }
        #endregion

        /// <summary>
        /// Asserts our desired soft-disconnect intent on one display: a watched display gets
        /// its effective configured value, an unwatched one false. Asserted ONLY when it
        /// differs from the display's current intent — that keeps platforms without soft
        /// disconnect inert (their intent reads false, so a desired false is a no-op), and a
        /// desired TRUE on such a platform is caught and warned once per display.
        /// One read-decide-write under the monitor lock: the startup sweep and a connect
        /// event may assert the same display concurrently, and recomputing the desired value
        /// inside the lock guarantees the LAST writer decided on the fresh watch state
        /// (tracking mutates under the same lock). Nesting into the platform manager is safe:
        /// every manager raises its events outside its own locks.
        /// </summary>
        private void AssertDisabled(IDisplay display)
        {
            lock (_lock)
            {
                bool desired = FindAnyWatch(display)?.ShouldBeDisabled ?? false;

                if (display.ShouldBeDisabled == desired)
                    return;

                try
                {
                    display.ShouldBeDisabled = desired;
                }
                catch (NotSupportedException ex)
                {
                    Logger.LogWarning($"Cannot hold display {display.Identity} disabled: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// The built-in panel has no connect/disconnect lifecycle — it is tracked once at
        /// startup and lives until shutdown. Its watch owns the lid subscription.
        /// </summary>
        private void TrackBuiltInDisplay(IDisplayBuiltIn builtIn, DisplayBuiltInDescriptor desc)
        {
            var scope = Scope.BeginLifetimeScope("Display", builder =>
            {
                builder.RegisterType<DisplayWatchBuiltIn>().AsSelf()
                    .WithParameter(TypedParameter.From(builtIn))
                    .WithParameter(TypedParameter.From(desc))
                    .SingleInstance();
            });

            if (scope.Resolve<DisplayWatchBuiltIn>() is { } watch && this.StartTracking(watch))
            {
                Logger.LogInformation($"Tracking built-in display: {builtIn.Identity}");

                Scope.Disposer.AddInstanceForDisposal(scope);
            }
            else
            {
                scope.Dispose();
            }
        }

        private void TrackDisplay(IDisplayExternal display, bool connect = false)
        {
            lock (_lock)
            {
                // same display reappearing within the debounce window? -> continue seamlessly
                // (the manager's identity guarantee: a reconnect IS the same instance,
                // so the watch just keeps running — nothing to swap in)
                if (FindPendingWatch(display) is DisplayWatchExternal pending)
                {
                    _pendingDisconnects.Remove(pending, out var cts);

                    cts!.Cancel();

                    Logger.LogDebug($"Display reappeared within debounce window: {display.Identity}");

                    return;
                }

                if (FindWatch(display) is not null)
                    return; // already tracked — the startup enumeration and a racing connect
                            // event may both deliver the same display (events subscribe first)

                if (!display.IsConnected)
                    return; // gone again before this call won the lock — its disconnect event
                            // found no watch to debounce, so tracking now would strand one

                if (!config!.ShouldWatch(display))
                {
                    return;
                }

                var scope = Scope.BeginLifetimeScope("Display", builder =>
                {
                    builder.RegisterType<DisplayWatchExternal>().AsSelf().SingleInstance();
                });

                var watch = scope.Resolve<DisplayWatchExternal>(TypedParameter.From(display));

                config.Configure(display, watch.ApplyConfiguration);

                if (this.StartTracking(watch))
                {
                    Logger.LogInformation($"Tracking display: {display.Identity}");

                    Scope.Disposer.AddInstanceForDisposal(scope);

                    _watchScopes[watch] = scope;

                    if (connect)
                    {
                        watch.TriggerConnect();
                    }
                }
                else
                {
                    scope.Dispose();
                }
            }
        }

        private async Task DebounceDisconnect(DisplayWatchExternal watch, CancellationToken token)
        {
            try
            {
                await Task.Delay(config!.DebounceTime, token);
            }
            catch (TaskCanceledException)
            {
                return; // display reappeared
            }

            lock (_lock)
            {
                if (!_pendingDisconnects.Remove(watch))
                    return;
            }

            Logger.LogInformation($"Display disconnected: {watch.Display.Identity}");

            watch.TriggerDisconnect();

            this.StopTracking(watch);

            lock (_lock)
            {
                if (_watchScopes.Remove(watch, out var scope))
                    scope.Dispose();
            }
        }

        #region Lookup helpers
        // reference equality suffices everywhere below — the manager guarantees a
        // reconnected display resurfaces as the very instance the watch already holds

        private DisplayWatch? FindAnyWatch(IDisplay display)
        {
            return this.FirstOrDefault(watch => watch.Display == display);
        }

        private DisplayWatchExternal? FindWatch(IDisplayExternal display)
        {
            return this.OfType<DisplayWatchExternal>().FirstOrDefault(watch => watch.Display == display);
        }

        private DisplayWatchExternal? FindPendingWatch(IDisplayExternal display)
        {
            return _pendingDisconnects.Keys.FirstOrDefault(watch => watch.Display == display);
        }
        #endregion

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            manager.DisplayConnected -= Manager_DisplayConnected;
            manager.DisplayDisconnected -= Manager_DisplayDisconnected;

            lock (_lock)
            {
                foreach (var cts in _pendingDisconnects.Values)
                    cts.Cancel();

                _pendingDisconnects.Clear();
            }

            // deliberately no revert of the asserted intents: the successor monitor's first
            // pass settles them against ITS configuration, and a full stop is cleaned up by
            // the manager itself (its dispose releases every hold it applied)
            foreach (var watch in this.ToArray())
                StopTracking(watch);
        }
    }
}
