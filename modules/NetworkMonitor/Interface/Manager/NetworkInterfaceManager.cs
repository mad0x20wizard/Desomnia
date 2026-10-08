using MadWizard.Desomnia.Application.Shutdown;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;

namespace MadWizard.Desomnia.Network.Interface.Manager
{
    /// <summary>
    /// Persistent interface inventory and platform operations. Observations never enforce
    /// configuration. Explicit writes preserve the latest external state for restoration
    /// across ephemeral application rebuilds and at process shutdown.
    /// </summary>
    public abstract class NetworkInterfaceManager : INetworkInterfaceManager, IAsyncStoppable, IDisposable
    {
        internal const string SSID_UNSUPPORTED = "This platform exposes no wireless information; "
            + "only a platform host's " + nameof(NetworkInterfaceManager) + " can answer an SSID.";

        public required ILogger Logger { protected get; init; }
        private readonly Lock _lock = new();
        private readonly Dictionary<NetworkIdentity, NetworkInterfaceImpl> _connected = [];
        private readonly InterfaceMemory _memory = new();
        private bool _disposed;

        internal int RememberedCount { get { lock (_lock) return _memory.Count; } }
        public event EventHandler<INetworkInterface>? InterfaceAttached;
        public event EventHandler<INetworkInterface>? InterfaceDetached;
        public event EventHandler? Changed;

        protected NetworkInterfaceManager()
        {
            Refresh();
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        }

        // Explicit polling updates observations without producing another reconciliation request.
        void INetworkInterfaceManager.Refresh() => Refresh(notify: false);

        public INetworkInterface? this[NetworkIdentity identity]
        {
            get { lock (_lock) return _connected.GetValueOrDefault(identity); }
        }

        IEnumerator<INetworkInterface> IEnumerable<INetworkInterface>.GetEnumerator()
        {
            lock (_lock) return _connected.Values.Cast<INetworkInterface>().ToList().GetEnumerator();
        }

        private void OnNetworkChanged(object? sender, EventArgs args)
        {
            try { Refresh(); }
            catch (Exception ex) { Logger.LogError(ex, "Failed to refresh network interfaces"); }
        }

        protected void Refresh(bool notify = true)
        {
            List<NetworkInterfaceImpl> attached = [], detached = [];
            lock (_lock)
            {
                if (_disposed) return;
                // Complete discovery before changing membership. Failure is not detachment.
                var snapshots = QueryInterfaces().ToArray();
                HashSet<NetworkIdentity> seen = [];
                foreach (var snapshot in snapshots)
                {
                    var identity = snapshot.ToIdentity();
                    if (!seen.Add(identity)) continue;
                    if (!_connected.TryGetValue(identity, out var handle))
                    {
                        handle = _memory.Recall(identity) ?? new NetworkInterfaceImpl(this, snapshot);
                        _connected.Add(identity, handle);
                        attached.Add(handle);
                    }
                    handle.Rebind(snapshot);
                    try { ObserveDisabled(handle); }
                    catch (Exception ex)
                    {
                        // A disappearing or inaccessible adapter must not interrupt inventory
                        // updates for the others. Its watch will retry the state read itself.
                        Logger?.LogWarning(ex, "Could not observe administrative state of {Interface}", handle.Name);
                    }
                }

                foreach (var identity in _connected.Keys.Where(id => !seen.Contains(id)).ToArray())
                {
                    var handle = _connected[identity];
                    _connected.Remove(identity);
                    // A genuine removal ends the old device's override. A returning device
                    // gets a fresh baseline and watch, while preserving handle identity.
                    handle.ForcedDisabled = handle.RestoreDisabled = handle.ObservedDisabled = handle.PendingDisabled = null;
                    _memory.Remember(handle);
                    detached.Add(handle);
                }
            }
            foreach (var handle in detached) InterfaceDetached?.Invoke(this, handle);
            foreach (var handle in attached) InterfaceAttached?.Invoke(this, handle);
            if (notify) Changed?.Invoke(this, EventArgs.Empty);
        }

        private bool ObserveDisabled(NetworkInterfaceImpl handle)
        {
            bool actual = IsInterfaceDisabled(handle);
            if (handle.PendingDisabled is bool pending)
            {
                // Native changes may become observable after the call returns. Until their
                // target is acknowledged, an old observation is not an external override.
                if (actual == pending) handle.PendingDisabled = null;
            }
            else if (handle.ObservedDisabled is bool previous && previous != actual)
            {
                handle.RestoreDisabled = actual;
                handle.ForcedDisabled = null;
            }
            if (handle.ForcedDisabled is null && handle.PendingDisabled is null)
                handle.RestoreDisabled = actual;
            handle.ObservedDisabled = actual;
            return actual;
        }

        internal bool ReadDisabled(NetworkInterfaceImpl handle)
        {
            lock (_lock)
            {
                if (_disposed || !_connected.ContainsKey(handle.Identity))
                    return handle.ObservedDisabled ?? throw new InvalidOperationException("Interface is no longer present.");
                return ObserveDisabled(handle);
            }
        }

        internal bool? ReadForcedDisabled(NetworkInterfaceImpl handle)
        {
            lock (_lock) return handle.ForcedDisabled;
        }

        internal void SetShouldBeDisabled(NetworkInterfaceImpl handle, bool? value)
        {
            lock (_lock)
            {
                if (_disposed || !_connected.ContainsKey(handle.Identity)) return;
                ApplyDisabled(handle, value);
            }
        }

        private void ApplyDisabled(NetworkInterfaceImpl handle, bool? value)
        {
            bool actual = ObserveDisabled(handle);
            if (value is null && handle.ForcedDisabled is null && handle.PendingDisabled is null) return;

            handle.RestoreDisabled ??= actual;
            bool target = value ?? handle.RestoreDisabled.Value;
            // Keep ownership on failure so a later release or shutdown can still restore it.
            if (value is not null) handle.ForcedDisabled = value;
            if (actual != target)
            {
                handle.PendingDisabled = target;
                if (target) DisableInterface(handle);
                else EnableInterface(handle);
                actual = ObserveDisabled(handle);
                if (actual != target)
                    throw new InvalidOperationException($"Interface '{handle.Name}' has not reached its requested administrative state.");
            }
            handle.PendingDisabled = null;
            handle.ForcedDisabled = value;
        }

        /// <summary>All physically present interfaces, including administratively disabled ones.</summary>
        protected virtual IEnumerable<NetworkInterface> QueryInterfaces() => NetworkInterface.GetAllNetworkInterfaces();
        protected abstract bool IsInterfaceDisabled(INetworkInterface @interface);
        protected abstract void DisableInterface(INetworkInterface @interface);
        protected abstract void EnableInterface(INetworkInterface @interface);
        protected virtual string? GetSSID(INetworkInterface @interface) => throw new NotSupportedException(SSID_UNSUPPORTED);

        internal string? QuerySSID(NetworkInterfaceImpl handle)
        {
            lock (_lock)
                if (!_connected.ContainsKey(handle.Identity)) return null;
            return GetSSID(handle);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Shutdown();
            return Task.CompletedTask;
        }

        public virtual void Dispose()
        {
            Shutdown();
            GC.SuppressFinalize(this);
        }

        private void Shutdown()
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var handle in _connected.Values)
                {
                    if (handle.ForcedDisabled is null && handle.PendingDisabled is null) continue;
                    try { ApplyDisabled(handle, null); }
                    catch (Exception ex) { Logger.LogError(ex, "Failed to restore interface {Interface}", handle.Name); }
                    finally { handle.ForcedDisabled = null; }
                }
            }
        }
    }
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

        // All administrative-state bookkeeping is accessed under the manager's lock.
        internal bool? ForcedDisabled;
        internal bool? RestoreDisabled;
        internal bool? ObservedDisabled;
        internal bool? PendingDisabled;

        public bool IsDisabled => _manager.ReadDisabled(this);
        public bool? ShouldBeDisabled
        {
            get => _manager.ReadForcedDisabled(this);
            set => _manager.SetShouldBeDisabled(this, value);
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
