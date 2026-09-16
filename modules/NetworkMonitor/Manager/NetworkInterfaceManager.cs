using MadWizard.Desomnia.Application.Shutdown;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Manager
{
    /// <summary>
    /// Shared implementation of <see cref="INetworkInterfaceManager"/> — the ONLY place that
    /// touches the <see cref="NetworkInterface"/>/<see cref="NetworkChange"/> statics.
    /// Platform hosts derive from it and provide the two OS operations (and, where the
    /// platform has one, the wireless lookup).
    ///
    /// The manager keeps one live handle per interface id and rebinds it to the latest OS
    /// snapshot on every network change; detached handles are remembered weakly (see
    /// <see cref="InterfaceMemory"/>), and handles with a standing
    /// <see cref="INetworkInterface.ShouldBeDisabled"/> intent are additionally held strongly,
    /// so the intent survives a disconnection — on Windows the disable itself removes the
    /// adapter from the enumeration, and it is exactly that hidden adapter whose intent must
    /// outlive it. Intents still applied when the manager is stopped (the persistent host's
    /// stop phase — see <see cref="IAsyncStoppable"/> — with disposal as the backstop) are
    /// restored, so a stopped Desomnia never leaves the machine without its interfaces.
    /// </summary>
    public abstract class NetworkInterfaceManager : INetworkInterfaceManager, IAsyncStoppable, IDisposable
    {
        internal const string SSID_UNSUPPORTED = "This platform exposes no wireless information; "
            + "only a platform host's " + nameof(NetworkInterfaceManager) + " can answer an SSID.";

        public required ILogger Logger { protected get; init; }

        private readonly Lock _lock = new();

        private readonly Dictionary<NetworkIdentity, NetworkInterfaceImpl> _connected = [];

        /// <summary>Strong holds: handles with a standing disable intent, keyed by identity —
        /// kept alive across a disconnection so the intent (and what we took away) survives.</summary>
        private readonly Dictionary<NetworkIdentity, NetworkInterfaceImpl> _intents = [];

        private readonly InterfaceMemory _memory = new();

        /// <summary>Set under the lock by <see cref="Shutdown"/> (the stop phase, or the
        /// <see cref="Dispose"/> backstop). Unsubscribing the NetworkChange statics does not
        /// stop an already-dispatched handler: a change in flight parks on the lock while
        /// the self-heal runs and proceeds once it is released — it must find a dead manager
        /// then, never re-apply an intent nobody is left to heal.</summary>
        private bool _disposed;

        /// <summary>How many detached handles the weak memory currently tracks — a test
        /// seam (like the protected <see cref="Refresh"/>) for the guarantee that
        /// intent-held handles never orphan entries there.</summary>
        internal int RememberedCount
        {
            get { lock (_lock) return _memory.Count; }
        }

        public event EventHandler<INetworkInterface>? InterfaceAttached;
        public event EventHandler<INetworkInterface>? InterfaceDetached;
        public event EventHandler? Changed;

        protected NetworkInterfaceManager()
        {
            Refresh(); // creation is gated (CreationTracker), so observing from birth is fine

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        #region INetworkInterfaceManager
        public INetworkInterface? this[NetworkIdentity identity]
        {
            get
            {
                lock (_lock)
                    return _connected.GetValueOrDefault(identity);
            }
        }

        IEnumerator<INetworkInterface> IEnumerable<INetworkInterface>.GetEnumerator()
        {
            List<INetworkInterface> snapshot;

            // every handle the manager knows — the connected interfaces plus the detached ones
            // an intent holds alive (NotPresent); an arbiter must see the latter to release a
            // standing intent, and a consumer wanting only live ones filters by Status
            lock (_lock)
                snapshot = [.. _connected.Values.Union(_intents.Values)];

            return snapshot.GetEnumerator();
        }
        #endregion

        #region snapshot refresh
        private void OnNetworkAddressChanged(object? sender, EventArgs e) => OnNetworkChanged();

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => OnNetworkChanged();

        private void OnNetworkChanged()
        {
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to refresh the network interface snapshot.");
            }
        }

        /// <summary>
        /// Re-reads the OS enumeration and diffs it against the handle table: present
        /// interfaces are rebound in place (the instance stays), returning ones are recalled
        /// from the strong intent table or the weak memory, vanished ones are detached — and
        /// every standing intent is reconciled against the new picture. Events are raised
        /// OUTSIDE the lock. Protected so a test subclass can pump fabricated changes.
        /// </summary>
        protected void Refresh()
        {
            List<NetworkInterfaceImpl> attached = [], detached = [];

            lock (_lock)
            {
                if (_disposed)
                    return; // a late change must not undo the dispose self-heal

                HashSet<NetworkIdentity> seen = [];

                foreach (var snapshot in QueryInterfaces())
                {
                    var identity = snapshot.ToIdentity();

                    if (!seen.Add(identity))
                        continue;

                    if (_connected.TryGetValue(identity, out var handle))
                    {
                        handle.Rebind(snapshot);
                    }
                    else
                    {
                        // the identity guarantee: a returning interface resurfaces as the
                        // remembered instance — a held one keeps its intent bookkeeping
                        if (_intents.TryGetValue(identity, out handle) || (handle = _memory.Recall(identity)) is not null)
                            handle.Rebind(snapshot);
                        else
                            handle = new NetworkInterfaceImpl(this, snapshot);

                        _connected[identity] = handle;

                        attached.Add(handle);
                    }
                }

                foreach (var identity in _connected.Keys.Where(id => !seen.Contains(id)).ToList())
                {
                    _connected.Remove(identity, out var handle);

                    if (handle!.DisableApplied && !StillExists(handle))
                    {
                        // the interface died while disabled (dock USB NICs vanish across
                        // sleep): the state we took away died with it, and its re-enumerated
                        // successor starts fresh — only the intent itself lives on
                        Logger.LogInformation($"Network interface '{handle.Name}' no longer exists — nothing to restore.");

                        handle.DisableApplied = false;
                        handle.TookDown = false;
                    }

                    // an intent-held handle returns through the strong table, so a memory
                    // entry would never be recalled — it would only orphan there for the
                    // life of the intent (and Windows detaches on every applied disable)
                    if (!_intents.ContainsKey(identity))
                        _memory.Remember(handle);

                    detached.Add(handle);
                }

                // re-apply returning intents, re-assert enforced ones against foreign re-enables
                foreach (var handle in _connected.Values)
                {
                    if (handle.ShouldBeDisabledCore)
                        Reconcile(handle);
                }
            }

            foreach (var handle in detached)
                InterfaceDetached?.Invoke(this, handle);
            foreach (var handle in attached)
                InterfaceAttached?.Invoke(this, handle);

            Changed?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region intent + reconciliation
        internal void SetShouldBeDisabled(NetworkInterfaceImpl handle, bool value)
        {
            lock (_lock)
            {
                if (_disposed || handle.ShouldBeDisabledCore == value)
                    return;

                handle.ShouldBeDisabledCore = value;

                if (value)
                    _intents[handle.Identity] = handle;
                else
                    _intents.Remove(handle.Identity);

                Logger.LogDebug($"Network interface '{handle.Name}' should be {(value ? "disabled" : "enabled")}.");

                Reconcile(handle);
            }
        }

        internal void SetEnforceDisabled(NetworkInterfaceImpl handle, bool value)
        {
            lock (_lock)
            {
                if (_disposed || handle.EnforceDisabledCore == value)
                    return;

                handle.EnforceDisabledCore = value;

                if (value && handle.ShouldBeDisabledCore)
                    Reconcile(handle); // enforcement may have to catch up on a tolerated re-enable
            }
        }

        /// <summary>
        /// The single apply path, called under the lock — on an intent change, on a handle's
        /// attach, and on every refresh of an intent-carrying handle. It brings the interface
        /// in line with the intent wherever the OS allows it: a disable is applied while the
        /// interface is present (recording whether it actually took an Up state away), a
        /// release restores only what we took — an interface that was already down stays
        /// down, one that no longer exists is skipped. A foreign re-enable of an applied
        /// disable stands, unless <see cref="INetworkInterface.EnforceDisabled"/> makes this
        /// the one path that may disable the same interface twice.
        /// </summary>
        private void Reconcile(NetworkInterfaceImpl handle)
        {
            if (handle.ShouldBeDisabledCore)
            {
                if (!_connected.ContainsKey(handle.Identity))
                    return; // not in the enumeration now; the next attach reconciles

                if (!handle.DisableApplied)
                {
                    // only a state we actually take away is restored later: a disable that
                    // finds the interface already administratively disabled leaves it that way,
                    // while one that takes an enabled interface (even a merely disconnected one)
                    // out of service puts it back on release
                    handle.TookDown = !IsInterfaceDisabled(handle);

                    Disable(handle);
                }
                else if (handle.EnforceDisabledCore && handle.Status == OperationalStatus.Up)
                {
                    handle.TookDown = true; // the re-assert takes an Up state away again

                    Disable(handle);
                }
            }
            else if (handle.DisableApplied)
            {
                handle.DisableApplied = false;

                if (!handle.TookDown)
                {
                    Logger.LogDebug($"Network interface '{handle.Name}' was already down before it was disabled — leaving it down.");
                }
                else if (!StillExists(handle))
                {
                    Logger.LogInformation($"Network interface '{handle.Name}' no longer exists — nothing to restore.");
                }
                else
                {
                    try
                    {
                        EnableInterface(handle); // the intent gone -> back into service

                        Logger.LogInformation($"Enabled network interface '{handle.Name}'");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to enable network interface '{handle.Name}'.");
                    }
                }

                handle.TookDown = false;
            }
        }

        private void Disable(NetworkInterfaceImpl handle)
        {
            try
            {
                DisableInterface(handle);

                Logger.LogInformation($"Disabled network interface '{handle.Name}'");

                handle.DisableApplied = true;
            }
            catch (Exception ex)
            {
                // DisableApplied stays false — the next reconcile (any refresh) retries
                Logger.LogError(ex, $"Failed to disable network interface '{handle.Name}'.");
            }
        }
        #endregion

        #region platform seams
        /// <summary>
        /// The OS enumeration — the single place the <see cref="NetworkInterface"/> statics
        /// are touched. Virtual only so a test subclass can supply fabricated snapshots.
        /// </summary>
        protected virtual IEnumerable<NetworkInterface> QueryInterfaces() => NetworkInterface.GetAllNetworkInterfaces();

        /// <summary>
        /// Whether the system still knows the interface — consulted before a restore, and to
        /// decide whether a detached handle's imposed state died with its device. The default
        /// asks the OS enumeration, which is right on the Unixes (a disabled interface stays
        /// enumerated there, so absence means gone); Windows drops disabled adapters from the
        /// enumeration and its manager overrides this with a lookup that sees them.
        /// </summary>
        protected virtual bool StillExists(INetworkInterface @interface)
        {
            try
            {
                return QueryInterfaces().Any(other => other.Id == @interface.Identity.Id);
            }
            catch
            {
                return true; // if the system will not tell, attempt the restore and let it speak
            }
        }

        protected abstract void DisableInterface(INetworkInterface @interface);

        protected abstract void EnableInterface(INetworkInterface @interface);

        /// <summary>
        /// Whether the interface is administratively DISABLED, as opposed to merely down or
        /// disconnected — which report the same <see cref="OperationalStatus.Down"/>. This is
        /// what decides whether a disable takes an enabled interface out of service (so a
        /// release restores it) or finds one already disabled (left as it was). The
        /// platform-neutral base cannot tell the two apart from the BCL, so it treats anything
        /// that is not <see cref="OperationalStatus.Up"/> as already down; a host that can
        /// (Windows, through the CIM admin status) overrides this.
        /// </summary>
        protected virtual bool IsInterfaceDisabled(INetworkInterface @interface) => @interface.Status != OperationalStatus.Up;

        /// <summary>
        /// The wireless network the interface is joined to, or null when it is not a wireless
        /// interface or not associated. The .NET runtime exposes nothing of the sort, so this
        /// throws until a platform host (WLAN API on Windows, CoreWLAN on macOS, nl80211 on
        /// Linux) supplies it.
        /// </summary>
        /// <exception cref="NotSupportedException">Always, in the platform-neutral base.</exception>
        protected virtual string? GetSSID(INetworkInterface @interface) => throw new NotSupportedException(SSID_UNSUPPORTED);

        internal string? QuerySSID(NetworkInterfaceImpl handle)
        {
            lock (_lock)
                if (!_connected.ContainsKey(handle.Identity))
                    return null; // gone from the enumeration — nothing to ask the WLAN service

            return GetSSID(handle);
        }
        #endregion

        /// <summary>The stop phase (see <see cref="IAsyncStoppable"/>): the self-heal runs
        /// here, while the process lifetime still waits — on Windows, before the SCM is told
        /// the service stopped. The restore is synchronous OS work (CIM on Windows); the
        /// token cannot interrupt it mid-call, the shutdown timeout bounds it from outside.</summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Shutdown();

            return Task.CompletedTask;
        }

        /// <summary>Backstop for a teardown that never ran the stop phase (tests, a faulted
        /// host); after <see cref="StopAsync"/> did its work, this is a no-op.</summary>
        public virtual void Dispose()
        {
            Shutdown();

            GC.SuppressFinalize(this);
        }

        private void Shutdown() // self-heal: never leave interfaces disabled behind
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

            lock (_lock)
            {
                if (_disposed)
                    return; // the stop phase already healed; the disposal backstop finds nothing to do

                _disposed = true;

                foreach (var handle in _intents.Values)
                {
                    if (!handle.DisableApplied)
                        continue; // the intent was never applied — nothing of ours in force

                    if (!handle.TookDown)
                    {
                        Logger.LogDebug($"Network interface '{handle.Name}' was already down before it was disabled — leaving it down.");

                        continue;
                    }

                    if (!StillExists(handle))
                    {
                        Logger.LogInformation($"Network interface '{handle.Name}' no longer exists — nothing to restore.");

                        continue;
                    }

                    try
                    {
                        EnableInterface(handle);

                        Logger.LogInformation($"Enabled network interface '{handle.Name}'");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to restore network interface '{handle.Name}' on shutdown.");
                    }
                }

                foreach (var handle in _intents.Values)
                {
                    // the intent dies with the manager: whoever still holds the handle must
                    // not find a flag a racing refresh could act on after the self-heal
                    handle.ShouldBeDisabledCore = false;
                    handle.EnforceDisabledCore = false;
                    handle.DisableApplied = false;
                    handle.TookDown = false;
                }

                _intents.Clear();
            }
        }
    }

    /// <summary>
    /// The live handle behind <see cref="INetworkInterface"/>: a stable identity wrapped
    /// around the latest OS snapshot. <see cref="Rebind"/> updates the backing data and
    /// keeps the instance — the manager's identity guarantee. The intent flags route to the
    /// manager, which owns locking and reconciliation.
    /// </summary>
    internal sealed class NetworkInterfaceImpl : INetworkInterface
    {
        private readonly NetworkInterfaceManager _manager;

        public NetworkIdentity Identity { get; }

        public string Name { get; private set; }

        public OperationalStatus Status { get; private set; }

        public NetworkInterfaceType Type { get; private set; }

        public PhysicalAddress PhysicalAddress { get; private set; } = PhysicalAddress.None;

        public IReadOnlyList<UnicastIPAddressInformation> Addresses { get; private set; } = [];

        public IReadOnlyList<GatewayIPAddressInformation> Gateways { get; private set; } = [];

        public string? DNSSuffix { get; private set; }

        public int? IPv6ScopeIndex { get; private set; }

        public string? SSID => _manager.QuerySSID(this);

        /// <summary>Guarded by the manager's lock, like all intent bookkeeping below.</summary>
        internal bool ShouldBeDisabledCore;

        internal bool EnforceDisabledCore;

        /// <summary>Whether our disable is currently in force on the OS.</summary>
        internal bool DisableApplied;

        /// <summary>Whether the interface was actually up when we disabled it.</summary>
        internal bool TookDown;

        public bool ShouldBeDisabled
        {
            get => ShouldBeDisabledCore;
            set => _manager.SetShouldBeDisabled(this, value);
        }

        public bool EnforceDisabled
        {
            get => EnforceDisabledCore;
            set => _manager.SetEnforceDisabled(this, value);
        }

        internal NetworkInterfaceImpl(NetworkInterfaceManager manager, NetworkInterface snapshot)
        {
            _manager = manager;

            Identity = snapshot.ToIdentity();

            Name = snapshot.Name;

            Rebind(snapshot);
        }

        /// <summary>Points the handle at the latest OS snapshot — the identity is equal by
        /// definition, everything else may differ.</summary>
        internal void Rebind(NetworkInterface snapshot)
        {
            Name = snapshot.Name;
            Status = snapshot.OperationalStatus;
            Type = snapshot.NetworkInterfaceType;
            PhysicalAddress = snapshot.GetPhysicalAddress() ?? PhysicalAddress.None;

            IPInterfaceProperties? properties = null;
            try
            {
                properties = snapshot.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                // some pseudo-interfaces refuse; the handle keeps empty address data
            }

            if (properties is null)
            {
                Addresses = [];
                Gateways = [];
                DNSSuffix = null;
                IPv6ScopeIndex = null;
            }
            else
            {
                // both are kept exactly as the OS reports them, scope ids and all
                Addresses = [.. properties.UnicastAddresses];
                Gateways = [.. properties.GatewayAddresses];

                try
                {
                    DNSSuffix = properties.DnsSuffix is { Length: > 0 } suffix ? suffix : null;
                }
                catch (PlatformNotSupportedException)
                {
                    DNSSuffix = null; // Linux has no interface-level suffix to offer
                }

                try
                {
                    IPv6ScopeIndex = snapshot.Supports(NetworkInterfaceComponent.IPv6) ? properties.GetIPv6Properties().Index : null;
                }
                catch (NetworkInformationException)
                {
                    IPv6ScopeIndex = null;
                }
            }
        }

        public override string ToString() => Name;
    }
}
