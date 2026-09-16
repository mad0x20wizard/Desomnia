using MadWizard.Desomnia.Application.Shutdown;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace MadWizard.Desomnia.Display.Manager
{
    /// <summary>
    /// Windows implementation of <see cref="IDisplayManager"/>, built on the PnP device stack
    /// (session-0 safe, probe-validated):
    ///
    ///  - Enumeration: present GUID_DEVINTERFACE_MONITOR interfaces; identity from the devnode's
    ///    EDID. Virtual/remote displays (RDP IddCx etc.) are filtered out — their adapters
    ///    enumerate under SWD/ROOT and they carry no EDID.
    ///  - Hot-plug: CM_Register_Notification (callback-based, no window needed).
    ///  - Power state: GUID_CONSOLE_DISPLAY_STATE (the OS blanking the console displays) applied
    ///    to every physical display. A sink that keeps its link alive while switched off (HDMI
    ///    TVs behind AVRs) is NOT detectable — the state only reflects what Windows drives.
    ///  - Connector type / built-in panel: WmiMonitorConnectionParams (root\wmi, needs SYSTEM/admin).
    ///  - Lid: GUID_LIDSWITCH_STATE_CHANGE, surfaced on the built-in display.
    /// </summary>
    public class WindowsDisplayManager : IDisplayManager, IAsyncStoppable, IDisposable
    {
        public required ILogger<WindowsDisplayManager> Logger { protected get; init; }

        private readonly object _lock = new();

        private Dictionary<string, WindowsDisplay>? _displays; // keyed by lower-cased symbolic link

        // the identity guarantee: disconnected displays stay recallable while referenced,
        // so a reconnect resurfaces the same instance (guarded by _lock, like _displays)
        private readonly DisplayMemory<WindowsExternalDisplay> _memory = new();

        // the built-in panel is singular, so its retention needs no identity table — a
        // devnode re-arrival (graphics driver restart) must resurface the same instance,
        // or the lid pipeline would be stranded on a dead one. Deliberately a STRONG
        // reference, unlike the external memory: lid consumers subscribe without holding
        // the display, and the panel always comes back — one retained object, never a
        // GC-timing lottery (guarded by _lock)
        private WindowsBuiltInDisplay? _rememberedBuiltIn;

        private nint _interfaceNotification;
        private readonly List<nint> _powerNotifications = [];

        // re-registered on every wake-up (see RefreshLidState), hence tracked separately
        private nint _lidNotification;
        private nint _resumeNotification;

        // rooted for the lifetime of the registration
        private CfgMgr32.NotifyCallback? _interfaceCallback;

        private bool? _consolePower;
        private bool? _lidOpen;

        public event EventHandler<IDisplayExternal>? DisplayConnected;
        public event EventHandler<IDisplayExternal>? DisplayDisconnected;

        private Dictionary<string, WindowsDisplay> Displays
        {
            get
            {
                lock (_lock)
                {
                    if (_displays == null)
                    {
                        _displays = [];

                        var connections = QueryConnectionTypes();

                        foreach (string symbolicLink in CfgMgr32.GetPresentInterfaces(CfgMgr32.GUID_DEVINTERFACE_MONITOR))
                        {
                            if (CreateDisplay(symbolicLink, connections) is WindowsDisplay display)
                            {
                                _displays[Key(symbolicLink)] = display;

                                // fresh instances, no subscribers yet — safe under the lock
                                display.UpdateOnline(_consolePower);

                                if (display is WindowsBuiltInDisplay builtIn)
                                    builtIn.UpdateLidOpen(_lidOpen);
                            }
                        }

                        Logger.LogDebug("Enumerating physical displays:");

                        foreach (var display in _displays.Values)
                        {
                            Logger.LogDebug($"{display}");
                        }

                        RegisterNotifications();

                        Logger.LogDebug($"Startup of {GetType().Name} complete.");
                    }

                    return _displays;
                }
            }
        }

        public IDisplayBuiltIn? BuiltIn
        {
            get
            {
                lock (_lock)
                    return Displays.Values.OfType<IDisplayBuiltIn>().FirstOrDefault();
            }
        }

        IEnumerator<IDisplay> IEnumerable<IDisplay>.GetEnumerator()
        {
            List<IDisplay> snapshot;

            lock (_lock)
                snapshot = [.. Displays.Values];

            return snapshot.GetEnumerator();
        }

        private static string Key(string symbolicLink) => symbolicLink.ToLowerInvariant();

        #region display creation
        private WindowsDisplay? CreateDisplay(string symbolicLink, IReadOnlyDictionary<string, long> connections)
        {
            try
            {
                if (CfgMgr32.GetInterfaceInstanceId(symbolicLink) is not string instanceId)
                    return null;

                uint devInst = CfgMgr32.LocateDevNode(instanceId);

                // virtual/indirect display adapters (RDP "Remote Display Adapter" etc.) enumerate
                // under the software device tree — only PnP-bus adapters drive physical monitors
                string? adapterEnumerator = CfgMgr32.GetParentEnumerator(devInst);

                if (adapterEnumerator is "SWD" or "ROOT")
                {
                    Logger.LogDebug($"Ignoring virtual display: {instanceId} (adapter enumerator: {adapterEnumerator})");

                    return null;
                }

                // no EDID -> no identity; also filters remote monitors, which never carry one
                if (CfgMgr32.ReadEdid(devInst) is not byte[] bytes)
                {
                    Logger.LogDebug($"Ignoring display without EDID: {instanceId}");

                    return null;
                }

                var edid = new EDID(bytes);

                if (!edid.HasValidHeader)
                {
                    Logger.LogWarning($"Ignoring display with invalid EDID: {instanceId}");

                    return null;
                }

                DisplayConnection? connection = null;

                if (connections.TryGetValue(instanceId.ToLowerInvariant(), out long vot))
                    connection = MapConnection(vot);

                WindowsDisplay display;

                if (connection == DisplayConnection.Internal)
                {
                    if (_rememberedBuiltIn is WindowsBuiltInDisplay panel && panel.Identity == edid.ToIdentity())
                    {
                        panel.Rebind(symbolicLink, instanceId, edid);

                        Logger.LogDebug($"Built-in display re-arrived as the remembered instance: {panel}");

                        _rememberedBuiltIn = null;

                        display = panel;
                    }
                    else
                    {
                        display = new WindowsBuiltInDisplay(symbolicLink, instanceId, edid);
                    }
                }
                else if (_memory.Recall(edid.ToIdentity()) is WindowsExternalDisplay remembered)
                {
                    // the identity guarantee: a still-referenced display reconnects
                    // as the very same instance, re-bound to its new interface
                    remembered.Rebind(symbolicLink, instanceId, edid, connection);

                    Logger.LogDebug($"Display reconnected as the remembered instance: {remembered}");

                    display = remembered;
                }
                else
                {
                    display = new WindowsExternalDisplay(symbolicLink, instanceId, edid, connection);
                }

                // deliberately NOT seeded with the cached power/lid state here: a recalled
                // instance still has live subscribers, and raising IsOnlineChanged under the
                // manager lock would run their handlers on the notification thread —
                // the call sites seed outside the lock instead
                return display;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"Failed to examine display interface: {symbolicLink}");

                return null;
            }
        }
        #endregion

        #region connector types (root\wmi)
        /// <summary>Maps devnode instance id (lower-cased) to D3DKMDT_VIDEO_OUTPUT_TECHNOLOGY.</summary>
        private Dictionary<string, long> QueryConnectionTypes()
        {
            var connections = new Dictionary<string, long>();

            try
            {
                using var session = CimSession.Create(null);

                foreach (var instance in session.QueryInstances(@"root\wmi", "WQL", "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams"))
                    using (instance)
                    {
                        if (instance.CimInstanceProperties["InstanceName"]?.Value is not string instanceName)
                            continue;

                        // WMI instance names carry a "_0" suffix on top of the devnode instance id
                        int suffix = instanceName.LastIndexOf('_');
                        if (suffix > 0)
                            instanceName = instanceName[..suffix];

                        connections[instanceName.ToLowerInvariant()] = Convert.ToInt64(instance.CimInstanceProperties["VideoOutputTechnology"]?.Value ?? -1L);
                    }
            }
            catch (Exception ex)
            {
                // connector type / built-in detection is enrichment, not foundation
                Logger.LogWarning($"Could not query monitor connection parameters: {ex.Message}");
            }

            return connections;
        }

        private static DisplayConnection MapConnection(long vot) => vot switch
        {
            0 => DisplayConnection.VGA,
            4 => DisplayConnection.DVI,
            5 => DisplayConnection.HDMI,
            10 => DisplayConnection.DisplayPort,

            6 => DisplayConnection.Internal,          // LVDS
            11 => DisplayConnection.Internal,         // DisplayPort embedded (eDP)
            13 => DisplayConnection.Internal,         // UDI embedded
            0x80000000 => DisplayConnection.Internal, // D3DKMDT_VOT_INTERNAL

            _ => DisplayConnection.Other,
        };
        #endregion

        #region notifications
        private void RegisterNotifications()
        {
            _interfaceCallback = OnInterfaceEvent;
            _interfaceNotification = CfgMgr32.RegisterInterfaceNotification(CfgMgr32.GUID_DEVINTERFACE_MONITOR, _interfaceCallback);

            // both deliver their current value immediately upon registration;
            // the lid setting never fires on machines without a lid (-> stays Unknown)
            _powerNotifications.Add(PowerSettings.Register(PowerSettings.ConsoleDisplayState, OnConsoleDisplayState));

            _lidNotification = PowerSettings.Register(PowerSettings.LidSwitchStateChange, OnLidSwitch);

            _resumeNotification = PowerSettings.RegisterResume(RefreshLidState);
        }

        /// <summary>
        /// The lid switch does not notify while the machine is asleep, so a lid operated during
        /// sleep would leave the cached state stale. Re-registering the setting makes the OS
        /// deliver its current value again, which flows through <see cref="OnLidSwitch"/> exactly
        /// like a live transition — and <see cref="WindowsBuiltInDisplay.UpdateLidOpen"/> swallows
        /// it when nothing actually changed. That keeps the built-in display authoritative:
        /// every raise is a real transition, and no transition is missed across a sleep.
        /// </summary>
        private void RefreshLidState()
        {
            nint stale;

            lock (_lock)
            {
                if ((stale = _lidNotification) == 0)
                    return; // not registered (yet) — nothing to reconcile against

                _lidNotification = 0;
            }

            // deliberately unlocked: the registration delivers the current value right away,
            // and OnLidSwitch takes the same lock
            PowerSettings.Unregister(stale);

            nint refreshed = PowerSettings.Register(PowerSettings.LidSwitchStateChange, OnLidSwitch);

            lock (_lock)
                _lidNotification = refreshed;
        }

        private uint OnInterfaceEvent(nint hNotify, nint context, int action, nint eventData, uint eventDataSize)
        {
            try
            {
                if (CfgMgr32.GetEventSymbolicLink(eventData) is not string symbolicLink)
                    return 0;

                if (action == CfgMgr32.CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL)
                {
                    WindowsDisplay? display = null;

                    lock (_lock)
                    {
                        if (_displays != null && !_displays.ContainsKey(Key(symbolicLink)))
                        {
                            display = CreateDisplay(symbolicLink, QueryConnectionTypes());

                            if (display != null)
                                _displays[Key(symbolicLink)] = display;
                        }
                    }

                    // seeded outside the lock: a recalled instance still has subscribers,
                    // and a stale power state must surface as a real IsOnlineChanged
                    if (display != null)
                        SeedEnabled(display);

                    if (display is WindowsBuiltInDisplay builtIn)
                        SeedLidOpen(builtIn);

                    // the built-in panel is always present (enumerated at startup) and never
                    // "connects" — only external displays raise the hot-plug events
                    if (display is IDisplayExternal external)
                    {
                        Logger.LogInformation($"Display connected: {display}");

                        DisplayConnected?.Invoke(this, external);
                    }
                }
                else if (action == CfgMgr32.CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL)
                {
                    WindowsDisplay? display = null;

                    lock (_lock)
                    {
                        if (_displays?.Remove(Key(symbolicLink), out display) == true)
                        {
                            if (display is WindowsExternalDisplay gone)
                            {
                                gone.IsConnected = false;

                                _memory.Remember(gone);
                            }
                            else if (display is WindowsBuiltInDisplay panel)
                            {
                                _rememberedBuiltIn = panel;
                            }
                        }
                    }

                    if (display is IDisplayExternal external)
                    {
                        Logger.LogInformation($"Display disconnected: {display}");

                        DisplayDisconnected?.Invoke(this, external);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while processing display interface event");
            }

            return 0;
        }

        /// <summary>
        /// Seeds a (re-)arrived display with the cached console-power state, outside the
        /// lock — but re-applied until stable: a power callback racing this seed applies
        /// its own value to the already-inserted display, and whichever apply lands last,
        /// the loop only settles once the display carries the CURRENT state.
        /// </summary>
        private void SeedEnabled(WindowsDisplay display)
        {
            bool? state;

            lock (_lock)
                state = _consolePower;

            while (true)
            {
                display.UpdateOnline(state);

                lock (_lock)
                {
                    if (_consolePower == state)
                        return;

                    state = _consolePower;
                }
            }
        }

        /// <summary>Same convergence dance as <see cref="SeedEnabled"/>, for the lid.</summary>
        private void SeedLidOpen(WindowsBuiltInDisplay builtIn)
        {
            bool? open;

            lock (_lock)
                open = _lidOpen;

            while (true)
            {
                builtIn.UpdateLidOpen(open);

                lock (_lock)
                {
                    if (_lidOpen == open)
                        return;

                    open = _lidOpen;
                }
            }
        }

        private void OnConsoleDisplayState(uint value)
        {
            bool state = value != 0; // dimmed counts as on

            WindowsDisplay[] displays;

            lock (_lock)
            {
                _consolePower = state;

                displays = _displays != null ? [.. _displays.Values] : [];
            }

            Logger.LogDebug($"Console display state changed: {(state ? "on" : "off")}");

            // through the convergence loop, like the arrival seed: should deliveries of
            // this callback ever overlap, the stale one cannot land last
            foreach (var display in displays)
                SeedEnabled(display);
        }

        private void OnLidSwitch(uint value)
        {
            bool open = value != 0;

            WindowsDisplay[] displays;

            lock (_lock)
            {
                _lidOpen = open;

                displays = _displays != null ? [.. _displays.Values] : [];
            }

            Logger.LogDebug($"Lid switch changed: {(open ? "open" : "closed")}");

            // convergence matters here even single-threaded per registration: an in-flight
            // delivery of the OLD registration may overlap the swap in RefreshLidState —
            // whichever apply lands last, the loop settles on the current state
            foreach (var display in displays.OfType<WindowsBuiltInDisplay>())
                SeedLidOpen(display);
        }
        #endregion

        /// <summary>The stop phase (see <see cref="IAsyncStoppable"/>): the native
        /// notifications are unregistered while the process lifetime still waits — and a
        /// future Windows soft-disconnect restore belongs here too, ahead of SERVICE_STOPPED.</summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Shutdown();

            return Task.CompletedTask;
        }

        /// <summary>Backstop for a teardown that never ran the stop phase (tests, a faulted
        /// host); after <see cref="StopAsync"/> did its work, this is a no-op.</summary>
        public void Dispose() => Shutdown();

        private void Shutdown() // idempotent: every handle is zeroed/cleared as it is released
        {
            if (_interfaceNotification != 0)
            {
                CfgMgr32.UnregisterNotification(_interfaceNotification);

                _interfaceNotification = 0;
            }

            foreach (nint handle in _powerNotifications)
                PowerSettings.Unregister(handle);

            _powerNotifications.Clear();

            if (_resumeNotification != 0)
            {
                PowerSettings.UnregisterResume(_resumeNotification);

                _resumeNotification = 0;
            }

            nint lidNotification;

            lock (_lock)
            {
                lidNotification = _lidNotification;

                _lidNotification = 0;
            }

            if (lidNotification != 0)
                PowerSettings.Unregister(lidNotification);
        }
    }
}
