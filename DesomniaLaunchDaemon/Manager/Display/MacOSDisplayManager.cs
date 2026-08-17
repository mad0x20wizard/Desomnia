using MadWizard.Desomnia.LaunchDaemon.Native;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MadWizard.Desomnia.Display.Manager
{
    /// <summary>
    /// macOS (Apple Silicon) implementation of <see cref="IDisplayManager"/>, built on IOKit only
    /// (no WindowServer — daemon-safe). The model, mapped out by probes/DisplayProbe.MacOS:
    ///
    ///  - "AppleCLCD2" nodes are display PIPES (disp0 = built-in, dispextN = external ports).
    ///    They persist forever and emit no useful notifications — they are a passive property
    ///    store: DisplayAttributes/ProductAttributes (identity), native resolution, image size,
    ///    "external" flag, Transport (connector).
    ///  - "DCPAVServiceProxy" nodes are the event backbone: one exists per PHYSICALLY connected
    ///    display path. An external proxy arrives on physical connect and terminates ONLY on a
    ///    real physical-link loss — an unplug, or sleep (which software-disconnects the link).
    ///    A soft-disconnect NEVER terminates the proxy (it stays alive, emitting only link-state
    ///    messages), so proxy arrival/termination is an unambiguous physical connect/disconnect
    ///    signal. The embedded proxy (disp0) is the built-in panel's power (dark on sleep/clamshell).
    ///    Message 0xE0115006 on a proxy carries the link state (0 = down/asleep, 1/2 = driven).
    ///  - Lid state AND system sleep/wake both come from IOPMrootDomain general interest
    ///    ("AppleClamshellState" + clamshell change message; kIOMessageSystemWillSleep /
    ///    HasPoweredOn) — the same notification the display probe uses, so this manager needs no
    ///    IPowerManager. The clamshell message is not delivered while asleep, so it is re-read
    ///    on wake as a backstop.
    ///  - Soft connect/disconnect goes the other way: setting IDisplay.ShouldBeDisabled
    ///    records our intention and the manager applies it — a disable at once (the panel is
    ///    drivable), an enable at once if the link is up else the moment the DCP proxy returns
    ///    (event-driven, never a timer). The resulting link-state message confirms it in IsOnline.
    ///    The built-in panel is a full soft-disconnect citizen (BetterDisplay parity): its
    ///    physical-presence gate is the embedded proxy, its CG id comes from CGDisplayIsBuiltin.
    ///
    /// All callbacks run on a dedicated CFRunLoop thread (see <see cref="RunLoopThread"/>);
    /// the lock guards cross-thread readers.
    /// </summary>
    public partial class MacOSDisplayManager : RunLoopThread, IDisplayManager, IAsyncStoppable
    {
        private const string DISPLAY_PIPE_CLASS = "AppleCLCD2";
        private const string AV_SERVICE_CLASS = "DCPAVServiceProxy";

        public required ILogger<MacOSDisplayManager> Logger { protected get; init; }

        private readonly object _lock = new();

        private nint _notifyPort;

        private uint _rootDomain;
        private uint _rootDomainNotification;
        private uint _matchIterator;
        private uint _terminateIterator;

        private readonly Dictionary<string, uint> _pipes = [];              // pipe token -> AppleCLCD2 service (retained)
        private readonly Dictionary<uint, ProxyInfo> _proxies = [];         // live DCPAVServiceProxy services
        private readonly Dictionary<string, MacOSDisplay> _displays = [];   // pipe token -> connected display

        // the identity guarantee: disconnected displays stay recallable while referenced,
        // so a reconnect resurfaces the same instance (guarded by _lock, like _displays)
        private readonly DisplayMemory<MacOSExternalDisplay> _memory = new();

        // explicit soft-disconnect holds, keyed by identity — a STRONG reference that keeps a
        // held display alive (and its stable CG id) across a physical disconnect, so a redock
        // returns it to the state we hold it in. Internal = our own hold; External = a foreign
        // one (e.g. BetterDisplay) we track for the full picture but never override.
        private readonly Dictionary<DisplayIdentity, DisplayHold> _holds = [];

        // the identity last connected on each external pipe — lets a returning display be
        // reconnected on its proxy arrival even before the pipe's DisplayAttributes repopulate
        private readonly Dictionary<string, DisplayIdentity> _lastOnPipe = [];

        // WindowServer refuses to commit configurations while a wake is still in flight
        // (kCGErrorIllegalArgument) and there is NO observable "ready" signal — the proxy arrival
        // AND kIOMessageSystemHasPoweredOn both precede it by an unpredictable margin. A disabled
        // display that fails to enable sends no further link-state message to reconcile on, so a
        // failed apply drives this bounded, self-terminating retry — the "watch the enable over
        // time" leg. Disabling (before sleep) never hits it, and a successful apply pays nothing.
        private static readonly TimeSpan RETRY_INTERVAL = TimeSpan.FromMilliseconds(500);
        private const int MAX_RETRY_ATTEMPTS = 20; // ~10s — generously covers the mid-wake window

        // How long a link must stay down before a soft-disconnect we did not initiate is believed.
        // 0xE0115006 reports the LINK, not anyone's intention, and a perfectly healthy link goes
        // down and re-trains on its own every few seconds (observed live on an LG HDR 4K over
        // DisplayPort: ~1.6s down, ~3.4s up, indefinitely) — indistinguishable from a foreign
        // disable in the single message. What tells them apart is only how long the link stays
        // down: a real hold keeps it down until someone lifts it, so an offline episode must
        // outlive this window to count. Releasing needs no such wait — link-up is unambiguous.
        private static readonly TimeSpan FOREIGN_HOLD_SETTLE = TimeSpan.FromSeconds(15);

        private Task? _retry;
        private readonly CancellationTokenSource _shutdown = new();

        // displays whose current offline episode is being waited out (see FOREIGN_HOLD_SETTLE) —
        // one wait per display, so a repeated link-down message cannot arm a second one
        private readonly HashSet<MacOSDisplay> _settling = [];

        private bool? _lidOpen;

        private sealed record ProxyInfo(string Pipe, bool Embedded, uint Notification);

        private enum HoldKind { Internal, External }

        private sealed record DisplayHold(MacOSDisplay Display, HoldKind Kind);

        public event EventHandler<IDisplayExternal>? DisplayConnected;
        public event EventHandler<IDisplayExternal>? DisplayDisconnected;

        [GeneratedRegex(@"disp(?:ext)?\d+")]
        private static partial Regex PipeToken();

        #region startup
        protected override void Initialize()
        {
            _notifyPort = IOKit.IONotificationPortCreate(IOKit.kIOMainPortDefault);

            CF.CFRunLoopAddSource(RunLoop, IOKit.IONotificationPortGetRunLoopSource(_notifyPort), CF.RunLoopDefaultMode);

            InitializeClamshell();

            EnumeratePipes();

            InitializeBuiltIn();

            RegisterProxyNotifications(); // drains already-present proxies

            Logger.LogDebug("Enumerating physical displays:");

            foreach (var display in _displays.Values)
            {
                Logger.LogDebug($"{display}");
            }

            Logger.LogDebug($"Startup of {GetType().Name} complete.");
        }

        private unsafe void InitializeClamshell()
        {
            _rootDomain = IOKit.FindService("IOPMrootDomain");

            if (_rootDomain == 0)
                return;

            _lidOpen = ReadClamshellState();

            int rc = IOKit.IOServiceAddInterestNotification(_notifyPort, _rootDomain, IOKit.kIOGeneralInterest,
                (nint)(delegate* unmanaged<nint, uint, uint, nint, void>)&OnRootDomainInterestCallback,
                RefCon, out _rootDomainNotification);

            if (rc != 0)
                Logger.LogWarning($"Could not register for clamshell notifications (0x{rc:X})");
        }

        private bool? ReadClamshellState() => IOKit.GetBooleanProperty(_rootDomain, "AppleClamshellState") switch
        {
            true => false,  // clamshell closed -> lid closed
            false => true,  // clamshell open -> lid open
            null => null,   // no lid (desktop Macs)
        };

        private void EnumeratePipes()
        {
            int rc = IOKit.IOServiceGetMatchingServices(IOKit.kIOMainPortDefault, IOKit.IOServiceMatching(DISPLAY_PIPE_CLASS), out uint iterator);
            if (rc != 0)
                throw new Exception($"IOServiceGetMatchingServices({DISPLAY_PIPE_CLASS}) failed (0x{rc:X})");

            try
            {
                for (uint service; (service = IOKit.IOIteratorNext(iterator)) != 0;)
                {
                    string path = IOKit.GetPath(service);

                    if (PipeToken().Match(path) is { Success: true } match && !_pipes.ContainsKey(match.Value))
                    {
                        _pipes[match.Value] = service; // handle intentionally kept: pipes persist for the process lifetime
                    }
                    else
                    {
                        IOKit.IOObjectRelease(service);
                    }
                }
            }
            finally
            {
                IOKit.IOObjectRelease(iterator);
            }

            if (_pipes.Count == 0)
            {
                Logger.LogWarning($"No {DISPLAY_PIPE_CLASS} display pipes found. " +
                                  "Only Apple Silicon Macs are supported (the Intel IODisplayConnect API is not implemented).");
            }
        }

        private void InitializeBuiltIn()
        {
            // the built-in panel is physically connected even when dark (clamshell mode) ->
            // enumerate it from its pipe right away; its power follows the embedded AV proxy
            foreach ((string pipe, uint clcd2) in _pipes)
            {
                if (IOKit.GetBooleanProperty(clcd2, "external") != true && CreateDisplay(pipe) is MacOSBuiltInDisplay display)
                {
                    display.UpdateOnline(false); // embedded proxy arrival flips it on
                    display.UpdateLidOpen(_lidOpen);

                    _displays[pipe] = display;

                    break;
                }
            }
        }

        private unsafe void RegisterProxyNotifications()
        {
            nint self = RefCon;

            int rc = IOKit.IOServiceAddMatchingNotification(_notifyPort, IOKit.kIOFirstMatchNotification, IOKit.IOServiceMatching(AV_SERVICE_CLASS),
                (nint)(delegate* unmanaged<nint, uint, void>)&OnProxyMatchedCallback, self, out _matchIterator);
            if (rc != 0)
                throw new Exception($"IOServiceAddMatchingNotification({AV_SERVICE_CLASS}) failed (0x{rc:X})");

            DrainArrivals(_matchIterator); // arms the notification + delivers already-present proxies

            rc = IOKit.IOServiceAddMatchingNotification(_notifyPort, IOKit.kIOTerminatedNotification, IOKit.IOServiceMatching(AV_SERVICE_CLASS),
                (nint)(delegate* unmanaged<nint, uint, void>)&OnProxyTerminatedCallback, self, out _terminateIterator);
            if (rc != 0)
                throw new Exception($"IOServiceAddMatchingNotification({AV_SERVICE_CLASS}, terminate) failed (0x{rc:X})");

            DrainTerminations(_terminateIterator);
        }
        #endregion

        #region IDisplayManager
        public IDisplayBuiltIn? BuiltIn
        {
            get
            {
                EnsureStarted();

                lock (_lock)
                    return _displays.Values.OfType<IDisplayBuiltIn>().FirstOrDefault();
            }
        }

        IEnumerator<IDisplay> IEnumerable<IDisplay>.GetEnumerator()
        {
            EnsureStarted();

            List<IDisplay> snapshot;

            lock (_lock)
                snapshot = [.. _displays.Values];

            return snapshot.GetEnumerator();
        }
        #endregion

        #region native callbacks (run on the CFRunLoop thread)
        [UnmanagedCallersOnly]
        private static void OnProxyMatchedCallback(nint refCon, uint iterator) => Self<MacOSDisplayManager>(refCon).DrainArrivals(iterator);

        [UnmanagedCallersOnly]
        private static void OnProxyTerminatedCallback(nint refCon, uint iterator) => Self<MacOSDisplayManager>(refCon).DrainTerminations(iterator);

        [UnmanagedCallersOnly]
        private static void OnProxyInterestCallback(nint refCon, uint service, uint messageType, nint messageArgument) => Self<MacOSDisplayManager>(refCon).OnProxyMessage(service, messageType, messageArgument);

        [UnmanagedCallersOnly]
        private static void OnRootDomainInterestCallback(nint refCon, uint service, uint messageType, nint messageArgument) => Self<MacOSDisplayManager>(refCon).OnRootDomainMessage(messageType, messageArgument);
        #endregion

        #region proxy lifecycle
        private void DrainArrivals(uint iterator)
        {
            // always drain to exhaustion — an un-drained iterator leaves the notification un-armed
            for (uint service; (service = IOKit.IOIteratorNext(iterator)) != 0;)
            {
                try
                {
                    OnProxyArrived(service);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error while processing AV service arrival");

                    IOKit.IOObjectRelease(service);
                }
            }
        }

        private unsafe void OnProxyArrived(uint service) // takes ownership of the service handle
        {
            string path = IOKit.GetPath(service);

            if (PipeToken().Match(path) is not { Success: true } match)
            {
                Logger.LogDebug($"Ignoring AV service without pipe token: {path}");

                IOKit.IOObjectRelease(service);

                return;
            }

            string pipe = match.Value;

            bool embedded = IOKit.GetStringProperty(service, "Location") is string location
                ? location == "Embedded"
                : !pipe.StartsWith("dispext");

            // watch the link-state messages of this display path
            int rc = IOKit.IOServiceAddInterestNotification(_notifyPort, service, IOKit.kIOGeneralInterest,
                (nint)(delegate* unmanaged<nint, uint, uint, nint, void>)&OnProxyInterestCallback,
                RefCon, out uint notification);

            if (rc != 0)
            {
                Logger.LogWarning($"Could not register for link-state notifications of {pipe} (0x{rc:X})");

                notification = 0;
            }

            MacOSExternalDisplay? connected = null;
            MacOSBuiltInDisplay? builtIn = null;

            lock (_lock)
            {
                _proxies[service] = new ProxyInfo(pipe, embedded, notification);

                if (embedded)
                {
                    builtIn = _displays.Values.OfType<MacOSBuiltInDisplay>().FirstOrDefault();

                    // the built-in panel is enumerated at startup and never raises a connect
                    // event; the embedded proxy only carries its power state
                    if (builtIn == null && !_displays.ContainsKey(pipe) && CreateDisplay(pipe) is MacOSBuiltInDisplay panel)
                    {
                        panel.UpdateLidOpen(_lidOpen);

                        builtIn = panel;
                        _displays[pipe] = panel;
                    }

                    if (builtIn != null)
                        builtIn.EmbeddedProxyPresent = true; // the panel's physical-presence gate
                }
                else if (!_displays.ContainsKey(pipe))
                {
                    // a physical connect. CreateDisplay recalls the same instance (with its
                    // remembered hold) when the identity is readable — but a returning display's
                    // DisplayAttributes can lag the proxy arrival, especially when it comes back
                    // soft-disabled and never trains the link (so no link-state message follows to
                    // retry the creation). The proxy arrival IS the physical-connect signal, so
                    // fall back to the display last seen on this pipe rather than losing it.
                    if ((CreateDisplay(pipe) as MacOSExternalDisplay ?? RecallOnPipe(pipe)) is MacOSExternalDisplay display)
                    {
                        _lastOnPipe[pipe] = display.Identity;
                        _displays[pipe] = display;
                        connected = display;
                    }
                    else
                    {
                        Logger.LogDebug($"AV proxy arrived on {pipe} but no display is identifiable there yet.");
                    }
                }
            }

            builtIn?.UpdateOnline(true); // panel lit (lid open / wake)

            if (builtIn != null)
            {
                CacheDisplayId(builtIn);

                Reconcile(builtIn); // apply any remembered soft-disconnect hold to the returning panel
            }

            if (connected != null)
            {
                CacheDisplayId(connected);

                Logger.LogInformation($"Display connected: {connected}");

                DisplayConnected?.Invoke(this, connected);

                Reconcile(connected); // apply any remembered soft-disconnect hold to the returning link
            }
        }

        private void DrainTerminations(uint iterator)
        {
            // always drain to exhaustion — an un-drained iterator leaves the notification un-armed
            for (uint service; (service = IOKit.IOIteratorNext(iterator)) != 0;)
            {
                try
                {
                    OnProxyTerminated(service);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error while processing AV service termination");
                }
                finally
                {
                    IOKit.IOObjectRelease(service); // the iterator's reference
                }
            }
        }

        private void OnProxyTerminated(uint service)
        {
            ProxyInfo? info;
            MacOSExternalDisplay? disconnected = null;
            MacOSBuiltInDisplay? builtIn = null;

            lock (_lock)
            {
                if (!_proxies.Remove(service, out info))
                    return;

                if (info.Embedded)
                {
                    // the embedded proxy IS the built-in panel's power — it terminates when the
                    // panel goes dark (sleep / clamshell) and re-arrives on wake
                    builtIn = _displays.Values.OfType<MacOSBuiltInDisplay>().FirstOrDefault();

                    if (builtIn != null)
                        builtIn.EmbeddedProxyPresent = false; // dark panel: nothing to apply to
                }
                else if (_displays.Remove(info.Pipe, out MacOSDisplay? removed) && removed is MacOSExternalDisplay external)
                {
                    // an external proxy terminates ONLY on a real physical-link loss — an unplug
                    // or sleep (which software-disconnects the link). A soft-disconnect never
                    // terminates the proxy, so this is always a genuine disconnect. Keep the
                    // cached CG id (stable across reconnect) and any hold/intention so a redock
                    // returns the display to the state we hold it in — the hold in _holds keeps
                    // the instance alive even if every other reference is released meanwhile.
                    external.IsConnected = false;

                    _memory.Remember(external);

                    disconnected = external;
                }
            }

            if (info.Notification != 0)
                IOKit.IOObjectRelease(info.Notification);

            IOKit.IOObjectRelease(service); // the reference kept since arrival

            builtIn?.UpdateOnline(false); // panel dark (clamshell mode / display sleep)

            if (disconnected != null)
            {
                Logger.LogInformation($"Display disconnected: {disconnected}");

                DisplayDisconnected?.Invoke(this, disconnected);
            }
        }

        private void OnProxyMessage(uint service, uint messageType, nint messageArgument)
        {
            try
            {
                if (messageType != IOMessage.kIOMessageDCPAVLinkState)
                    return;

                bool online = messageArgument != 0; // 0 = link down/soft-disabled, 1/2 = driven

                MacOSExternalDisplay? connected = null;
                MacOSDisplay? display = null;

                lock (_lock)
                {
                    if (!_proxies.TryGetValue(service, out ProxyInfo? info))
                        return;

                    if (_displays.TryGetValue(info.Pipe, out display))
                    {
                        display.UpdateOnline(online);
                    }
                    else if (!info.Embedded && online && CreateDisplay(info.Pipe) is MacOSExternalDisplay created)
                    {
                        // second chance: attributes were not populated at proxy arrival
                        _displays[info.Pipe] = created;
                        _lastOnPipe[info.Pipe] = created.Identity;
                        created.UpdateOnline(online);
                        connected = created;
                        display = created;
                    }

                    if (display != null)
                    {
                        // a display confirmed DRIVEN is no longer held by us — sync the committed
                        // flag to that reality, so a display that came back enabled (macOS did not
                        // keep our hold) is re-disabled if we still intend it, and a foreign
                        // disable is not mislabeled as ours
                        if (online)
                            display.DisableApplied = false;

                        TrackForeignHold(display); // a soft-disconnect we did not initiate
                    }
                }

                if (connected != null)
                {
                    CacheDisplayId(connected);

                    Logger.LogInformation($"Display connected: {connected}");

                    DisplayConnected?.Invoke(this, connected);
                }

                if (display != null)
                    Reconcile(display); // re-apply our own intention where the link now allows it
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while processing AV link-state message");
            }
        }

        /// <summary>
        /// One callback for the whole IOPMrootDomain general-interest stream — clamshell AND
        /// system sleep/wake, so the manager needs no IPowerManager (the display probe reads
        /// the very same notification). Everything arrives on the CFRunLoop thread in delivery
        /// order, so there is no cross-thread race between the wake and the lid re-read.
        /// </summary>
        private void OnRootDomainMessage(uint messageType, nint messageArgument)
        {
            try
            {
                switch (messageType)
                {
                    case IOMessage.kIOPMMessageClamshellStateChange:
                        // macOS re-announces the current clamshell state on all kinds of occasions
                        // (roughly hourly while closed, twice on an open) — ApplyLidState reports
                        // only the flips.
                        ApplyLidState((messageArgument & 1) == 0); // clamshell-closed bit set -> lid closed
                        break;

                    case IOMessage.kIOMessageSystemHasPoweredOn:
                        // clamshell messages are not delivered while asleep, so re-read the lid on
                        // wake as a backstop (ApplyLidState swallows a no-op). The displays return
                        // through their own proxy arrivals — event-driven — which re-apply holds;
                        // nothing here needs to poll or wait for the wake to settle.
                        Logger.LogTrace("System has powered on — reconciling the lid state.");

                        if (_rootDomain != 0)
                            ApplyLidState(ReadClamshellState());
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while processing an IOPMrootDomain message");
            }
        }

        private void ApplyLidState(bool? lidOpen)
        {
            MacOSBuiltInDisplay? builtIn;
            bool changed;

            lock (_lock)
            {
                changed = _lidOpen != lidOpen;
                _lidOpen = lidOpen;

                builtIn = _displays.Values.OfType<MacOSBuiltInDisplay>().FirstOrDefault();
            }

            if (changed)
                Logger.LogDebug($"Lid state changed: {Describe(lidOpen)}");

            builtIn?.UpdateLidOpen(lidOpen); // no-op unless the state actually changed
        }

        private static string Describe(bool? lidOpen) => lidOpen switch
        {
            true => "open",
            false => "closed",
            null => "unknown (no lid)",
        };
        #endregion

        #region display creation
        private MacOSDisplay? CreateDisplay(string pipe)
        {
            if (!_pipes.TryGetValue(pipe, out uint clcd2))
            {
                Logger.LogWarning($"AV service references unknown display pipe: {pipe}");

                return null;
            }

            nint attributes = IOKit.GetProperty(clcd2, "DisplayAttributes");

            if (attributes == 0)
                return null; // pipe (still) idle

            try
            {
                if (ReadIdentity(attributes) is not DisplayIdentity identity)
                    return null;

                Resolution? native = null;

                if (CF.GetNumber(attributes, "NativeFormatHorizontalPixels") is long width && CF.GetNumber(attributes, "NativeFormatVerticalPixels") is long height)
                    native = new Resolution((int)width, (int)height);

                string? edidUuid = IOKit.GetStringProperty(clcd2, "EDID UUID");

                if (IOKit.GetBooleanProperty(clcd2, "external") != true)
                    return new MacOSBuiltInDisplay(this, pipe, identity, native, edidUuid);

                if (_memory.Recall(identity) is MacOSExternalDisplay remembered)
                {
                    // the identity guarantee: a still-referenced display reconnects
                    // as the very same instance, re-bound to its new pipe
                    remembered.Rebind(pipe, native, edidUuid, MapConnection(clcd2));

                    Logger.LogDebug($"Display reconnected as the remembered instance: {remembered}");

                    return remembered;
                }

                return new MacOSExternalDisplay(this, pipe, identity, native, edidUuid, MapConnection(clcd2));
            }
            finally
            {
                CF.CFRelease(attributes);
            }
        }

        /// <summary>Parses the DisplayAttributes' ProductAttributes into the identity —
        /// side-effect-free, so it can also answer "who occupies this pipe NOW" without
        /// going through <see cref="CreateDisplay"/> (whose memory recall mutates state).</summary>
        private static DisplayIdentity? ReadIdentity(nint attributes)
        {
            nint product = CF.GetDictionary(attributes, "ProductAttributes");

            if (product == 0)
                return null;

            if (CF.GetString(product, "ManufacturerID") is not string vendor || CF.GetNumber(product, "ProductID") is not long productCode)
                return null;

            long? serial = CF.GetNumber(product, "SerialNumber");
            long? week = CF.GetNumber(product, "WeekOfManufacture");
            long? year = CF.GetNumber(product, "YearOfManufacture");

            string? serialString = CF.GetString(product, "AlphanumericSerialNumber");

            return new DisplayIdentity
            {
                VendorId = vendor,
                ProductCode = (ulong)productCode,
                Name = CF.GetString(product, "ProductName"),
                SerialNumber = serial is > 0 and <= uint.MaxValue ? (uint)serial.Value : null,
                SerialString = string.IsNullOrEmpty(serialString) ? null : serialString,
                WeekOfManufacture = week is > 0 and <= 54 ? (byte)week.Value : null,
                YearOfManufacture = year is > 0 ? (ushort)year.Value : null,
            };
        }

        /// <summary>
        /// Reconnects the external display last seen on a pipe when its DisplayAttributes have
        /// not repopulated at proxy-arrival time. The returning display on the same pipe is the
        /// same one, so recall it by that remembered identity (from the strong hold table, or the
        /// weak recall memory) and re-bind it with its last-known hardware info — the link
        /// training refreshes resolution/UUID later. Returns null when nothing is remembered here.
        /// </summary>
        private MacOSExternalDisplay? RecallOnPipe(string pipe)
        {
            if (!_lastOnPipe.TryGetValue(pipe, out DisplayIdentity? identity))
                return null;

            MacOSExternalDisplay? display = _holds.TryGetValue(identity, out var hold) ? hold.Display as MacOSExternalDisplay : _memory.Recall(identity);

            if (display == null)
                return null;

            display.Rebind(pipe, display.NativeResolution, display.EdidUuid, display.Connection);

            Logger.LogDebug($"Display reconnected on {pipe} before its attributes repopulated: {display}");

            return display;
        }

        private static DisplayConnection? MapConnection(uint clcd2)
        {
            nint transport = IOKit.GetProperty(clcd2, "Transport");

            if (transport == 0)
                return null;

            try
            {
                return CF.GetString(transport, "Downstream") switch
                {
                    "HDMI" => DisplayConnection.HDMI,
                    "DP" => DisplayConnection.DisplayPort,
                    "DVI" => DisplayConnection.DVI,
                    null => null,
                    _ => DisplayConnection.Other,
                };
            }
            finally
            {
                CF.CFRelease(transport);
            }
        }
        #endregion

        #region soft connect/disconnect (SkyLight)
        /// <summary>
        /// Resolves and caches the CG display id at connect time, while the display world
        /// is calm — looking it up during a lid transition has been observed to come up
        /// empty in BOTH directions (reverse lookup and online-list scan, macOS 15.5),
        /// so the soft-disconnect actions run on the cache. Failure is only logged;
        /// <see cref="Reconcile"/> retries the lookup lazily and, when even that comes up
        /// empty, hands the display to the bounded retry loop.
        /// </summary>
        private void CacheDisplayId(MacOSDisplay display)
        {
            if (display.CGDisplayId != 0)
                return;

            try
            {
                uint id = ResolveDisplayId(display);

                if (id != 0)
                {
                    lock (_lock)
                        display.CGDisplayId = id;

                    Logger.LogTrace($"Resolved CG display id {id}: {display}");
                }
                else
                {
                    Logger.LogDebug($"CoreGraphics does not (yet) know display {display}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"Could not resolve the CG display id of {display}");
            }
        }

        /// <summary>
        /// The UUID route is tried first (CG display UUID = EDID UUID, per the probe docs) —
        /// but in the daemon context WindowServer hands out RANDOM v4 UUIDs instead of
        /// EDID-derived ones (observed live, macOS 15.5), so the reliable bridge is the
        /// EDID attribute match: vendor/model(/serial) against the online display list.
        /// </summary>
        private uint ResolveDisplayId(MacOSDisplay display)
        {
            // the built-in panel has no EDID UUID on its pipe and no PnP vendor code, so
            // neither external route can name it — but WindowServer flags it directly
            if (display is MacOSBuiltInDisplay)
                return SkyLight.GetBuiltInDisplayId(Logger);

            if (display.EdidUuid is string uuid)
            {
                uint id = SkyLight.DisplayIdFromUuid(uuid, Logger);

                if (id != 0)
                    return id;
            }

            return MatchByIdentity(display);
        }

        private uint MatchByIdentity(MacOSDisplay display)
        {
            if (PnPVendorNumber(display.Identity.VendorId) is not uint vendor)
                return 0; // no PnP vendor code (e.g. the built-in panel's OUI string)

            var matches = SkyLight.GetOnlineDisplays(Logger)
                .Where(online => online.Vendor == vendor && online.Model == display.Identity.ProductCode)
                .ToArray();

            if (matches.Length > 1 && display.Identity.SerialNumber is uint serial)
                matches = [.. matches.Where(online => online.Serial == serial)];

            if (matches.Length == 1)
            {
                Logger.LogTrace($"Matched CG display id {matches[0].Id} by EDID attributes: {display}");

                return matches[0].Id;
            }

            if (matches.Length > 1) // identical twin panels — nothing left to tell them apart
                Logger.LogWarning($"Ambiguous CG display ids for {display}: {string.Join(", ", matches.Select(online => online.Id))}");

            return 0;
        }

        /// <summary>Encodes a three-letter PnP vendor code ("GSM") into its EDID numeric
        /// form (0x1E6D) — the value CGDisplayVendorNumber reports.</summary>
        private static uint? PnPVendorNumber(string vendorId)
        {
            if (vendorId.Length != 3)
                return null;

            uint value = 0;

            foreach (char c in vendorId)
            {
                if (c is < 'A' or > 'Z')
                    return null;

                value = value << 5 | (uint)(c - 'A' + 1);
            }

            return value;
        }

        /// <summary>
        /// Records our soft-disconnect intention for a display and reconciles at once.
        /// Disabling is applied immediately (the panel is present and drivable); enabling
        /// is applied immediately if the physical link is up, else the moment the display's DCP
        /// proxy returns (see <see cref="Reconcile"/>). App-scoped SkyLight: WindowServer
        /// restores every display if the daemon process dies.
        /// </summary>
        internal void SetShouldBeDisabled(MacOSDisplay display, bool shouldBeDisabled)
        {
            lock (_lock)
            {
                if (display.ShouldBeDisabled == shouldBeDisabled)
                    return; // nothing new to intend

                display.SetShouldBeDisabledIntent(shouldBeDisabled);

                if (shouldBeDisabled)
                    _holds[display.Identity] = new DisplayHold(display, HoldKind.Internal);
                else if (_holds.TryGetValue(display.Identity, out var hold) && hold.Kind == HoldKind.Internal)
                    _holds.Remove(display.Identity);
            }

            Logger.LogDebug($"Display should be soft-{(shouldBeDisabled ? "disconnected" : "connected")}: {display}");

            Reconcile(display);
        }

        /// <summary>
        /// Brings a display's actual soft state in line with our intention wherever the physical
        /// link allows it — the single apply path, called on an intention change, on the
        /// display's proxy arrival, and on its link-state messages. It never polls: a
        /// disconnected display is skipped, and its returning proxy calls back here. The decision
        /// is driven by our intention (<see cref="IDisplay.ShouldBeDisabled"/>) versus the
        /// committed <see cref="MacOSDisplay.DisableApplied"/> — NOT the observed IsOnline,
        /// which a returning disabled display never reports. DisableApplied persists across a
        /// reconnect (macOS keeps the hold), so a re-docked display's enable is re-applied; and a
        /// foreign hold is never overridden, because we only ever release a disable WE applied.
        /// An apply that cannot land yet — WindowServer refusing the commit, or a CG display id
        /// that will not resolve mid-transition — arms the bounded retry loop instead: neither
        /// failure is guaranteed a follow-up event of its own (a still-disabled display stays
        /// silent), so waiting for one could park the intent indefinitely.
        /// </summary>
        private void Reconcile(MacOSDisplay display)
        {
            bool enable;
            uint id;

            lock (_lock)
            {
                if (!display.IsPresent)
                    return; // no physical path right now — the proxy arrival will reconcile

                if (display.ShouldBeDisabled && !display.DisableApplied)
                    enable = false;
                else if (!display.ShouldBeDisabled && display.DisableApplied)
                    enable = true;
                else
                    return; // already committed to the intended state (or nothing of ours to undo)

                id = display.CGDisplayId;

                if (id == 0 && (id = ResolveDisplayId(display)) != 0)
                    display.CGDisplayId = id;
            }

            if (id == 0)
            {
                // the same transience as a refused commit: CG lookups come up empty during a
                // lid/wake transition (see CacheDisplayId), and a display that stays soft-disabled
                // never trains its link — no follow-up event is guaranteed to retry the resolve.
                // The retry loop re-enters Reconcile (NeedsReconcile describes exactly this
                // display) and terminates on its own attempt bound, so an id that never resolves
                // cannot spin it forever.
                Logger.LogDebug($"CoreGraphics does not (yet) know display {display} — deferring the soft-{(enable ? "connect" : "disconnect")}.");

                ScheduleRetry();

                return;
            }

            try
            {
                SkyLight.SetDisplayEnabled(id, enable, Logger);

                lock (_lock)
                    display.DisableApplied = !enable;

                Logger.LogInformation($"Display soft-{(enable ? "connected" : "disconnected")}: {display}");
            }
            catch (NotSupportedException ex)
            {
                // permanent (missing private symbols, or WindowServer unreachable) — say so
                Logger.LogWarning(ex, $"Soft connect/disconnect is unavailable for {display}.");
            }
            catch (Exception ex)
            {
                // transient — most often WindowServer refusing to commit mid-wake
                // (kCGErrorIllegalArgument), which has no observable "ready" signal and no
                // follow-up event to reconcile on (a still-disabled display stays silent), so
                // kick the bounded retry loop to try again until WindowServer takes it.
                Logger.LogDebug($"Could not soft-{(enable ? "connect" : "disconnect")} display {display} yet: {ex.Message}");

                ScheduleRetry();
            }
        }

        /// <summary>Ensures the retry loop is running after a transient apply failure. Started
        /// once; it re-reconciles every unsettled display at a fixed interval until all settle or
        /// the attempt bound is hit — no per-display timers, no work when nothing needs it.</summary>
        private void ScheduleRetry()
        {
            lock (_lock)
            {
                if (_shutdown.IsCancellationRequested)
                    return; // tearing down

                if (_retry == null || _retry.IsCompleted)
                    _retry = Task.Run(RetryLoop);
            }
        }

        private async Task RetryLoop()
        {
            try
            {
                for (int attempt = 0; attempt < MAX_RETRY_ATTEMPTS; attempt++)
                {
                    await Task.Delay(RETRY_INTERVAL, _shutdown.Token);

                    List<MacOSDisplay> pending;

                    lock (_lock)
                        pending = [.. _displays.Values.Where(NeedsReconcile)];

                    if (pending.Count == 0)
                        break; // every display has settled to its intended state

                    foreach (var display in pending)
                        Reconcile(display);
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down — the app-scoped configuration reverts by itself
            }
            finally
            {
                lock (_lock)
                    _retry = null;
            }
        }

        /// <summary>A present display whose committed state disagrees with our intention.</summary>
        private static bool NeedsReconcile(MacOSDisplay display) =>
            display.IsPresent && display.ShouldBeDisabled != display.DisableApplied;

        /// <summary>Tracks a foreign soft-disconnect (one we did not initiate) so Desomnia keeps
        /// the full picture — recorded as an External hold, never overridden by us. Called under
        /// <see cref="_lock"/> from the link-state handler. Detection is deferred by
        /// <see cref="FOREIGN_HOLD_SETTLE"/>, since a link-down message alone does not mean
        /// anyone disabled anything; the release is immediate.</summary>
        private void TrackForeignHold(MacOSDisplay display)
        {
            if (display.ShouldBeDisabled || display.DisableApplied)
                return; // this one is ours — intended OR still committed (a release in flight),
                        // never a foreign hold. Missing the DisableApplied half mislabels our own
                        // still-in-effect disable as external the moment we clear the intention.

            bool offline = display.IsOnline == false;
            bool tracked = _holds.TryGetValue(display.Identity, out var hold) && hold.Kind == HoldKind.External;

            if (offline && !tracked)
            {
                SettleForeignHold(display);
            }
            else if (!offline && tracked)
            {
                _holds.Remove(display.Identity);

                Logger.LogDebug($"Foreign soft-disconnect hold on {display} released.");
            }
        }

        /// <summary>
        /// Waits a link-down out and records the hold only once a whole
        /// <see cref="FOREIGN_HOLD_SETTLE"/> window passes within ONE offline episode. A link that
        /// re-trained meanwhile has bumped its <see cref="MacOSDisplay.LinkEpisode"/>, and — as
        /// long as it is still down — simply gets a fresh window rather than being dropped: a
        /// disable that lands mid-flapping produces no further message of its own, so nothing else
        /// would come along to re-arm the wait. Called under <see cref="_lock"/>.
        /// </summary>
        private void SettleForeignHold(MacOSDisplay display)
        {
            uint episode = display.LinkEpisode;

            if (_shutdown.IsCancellationRequested || !_settling.Add(display))
                return; // tearing down, or this display's link is already being waited out

            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(FOREIGN_HOLD_SETTLE, _shutdown.Token);

                        bool detected = false;

                        lock (_lock)
                        {
                            uint current = display.LinkEpisode;

                            if (IsUnclaimedOffline(display))
                            {
                                if (current != episode)
                                {
                                    episode = current; // still down, but a later episode — give it its own full window

                                    continue;
                                }

                                _holds[display.Identity] = new DisplayHold(display, HoldKind.External);

                                detected = true;
                            }

                            // the slot is released under the same lock that decided, so a
                            // link-state message can never slip in between and find nobody watching
                            _settling.Remove(display);
                        }

                        if (detected)
                            Logger.LogDebug($"Detected a foreign soft-disconnect hold on {display}.");

                        return;
                    }
                }
                catch (Exception ex)
                {
                    lock (_lock)
                        _settling.Remove(display);

                    if (ex is not OperationCanceledException) // cancelled = shutting down
                        Logger.LogWarning(ex, $"Could not settle the soft-disconnect state of {display}.");
                }
            });
        }

        /// <summary>Whether a display is soft-disconnected by someone other than us and is not
        /// recorded as held yet — the standing precondition of a foreign hold, re-checked after
        /// the settling wait because every part of it can change while we wait. Under <see cref="_lock"/>.</summary>
        private bool IsUnclaimedOffline(MacOSDisplay display) =>
            display.IsPresent && display.IsOnline == false
            && !display.ShouldBeDisabled && !display.DisableApplied
            && !_holds.ContainsKey(display.Identity);

        #endregion

        /// <summary>The stop phase (see <see cref="IAsyncStoppable"/>): stops the run-loop
        /// thread and runs <see cref="Cleanup"/> — the soft-disconnect restore — while the
        /// process lifetime still waits (launchd's SIGTERM window). The inherited
        /// <see cref="RunLoopThread.Dispose"/> remains the backstop; <see cref="RunLoopThread.Stop"/>
        /// is idempotent, so whichever runs second finds nothing to do.</summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stop();

            return Task.CompletedTask;
        }

        protected override void Cleanup()
        {
            _shutdown.Cancel(); // stop the retry loop

            // the manager lives in the persistent OS-adapter host, so Cleanup runs ONLY when the
            // whole application stops (a configuration rebuild keeps it alive). Return every
            // display WE hold soft-disconnected to normal, so a graceful stop leaves the machine
            // at a clean baseline instead of relying on the app-scoped SkyLight config reverting
            // at process exit — the user need not re-enable them by hand. Foreign holds are left.
            ReleaseInternalHolds();

            if (_notifyPort != 0)
            {
                IOKit.IONotificationPortDestroy(_notifyPort); // also invalidates all registrations

                _notifyPort = 0;
            }

            // remaining io_object handles (pipes, proxies, root domain) are process-lifetime;
            // the manager is a singleton disposed only at shutdown
        }

        private void ReleaseInternalHolds()
        {
            List<MacOSDisplay> held;

            lock (_lock)
                held = [.. _holds.Values.Where(hold => hold.Kind == HoldKind.Internal).Select(hold => hold.Display)];

            foreach (var display in held)
            {
                try
                {
                    if (display.CGDisplayId != 0)
                    {
                        SkyLight.SetDisplayEnabled(display.CGDisplayId, enabled: true, Logger);

                        Logger.LogInformation($"Restored soft-disconnected display on shutdown: {display}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"Could not restore soft-disconnected display {display} on shutdown.");
                }
            }
        }
    }
}
