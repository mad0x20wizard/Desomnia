using MadWizard.Desomnia.Configuration.Binding;
using MadWizard.Desomnia.Network.Configuration.Interfaces;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using System.Collections.Concurrent;

namespace MadWizard.Desomnia.Network.Interface
{
    /// <summary>
    /// Owns interface watches and coalesces manager notifications into serialized rounds.
    /// Each round publishes its result and awaits consumers before processing further changes.
    /// Overrides survive this ephemeral service; the persistent manager owns restoration.
    /// </summary>
    public sealed class NetworkInterfaceMonitor(IEnumerable<NetworkInterfaceWatchInfo> configurations) : System.IAsyncDisposable
    {
        public required INetworkInterfaceManager Manager { private get; init; }
        public required Func<InterfaceMatcher> CreateMatcher { private get; init; }
        public required ILogger<NetworkInterfaceMonitor> Logger { private get; init; }

        public event AsyncEventHandler? Changed;

        private readonly AsyncManualResetEvent _changed = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<INetworkInterface, NetworkInterfaceWatch> _watches = [];
        private readonly ConcurrentQueue<INetworkInterface> _detached = new();
        private List<(NetworkInterfaceWatchInfo Config, InterfaceMatcher Matcher)> _rules = [];
        private Task? _processing;
        private bool _disposed;

        private sealed record Snapshot(
            long Revision,
            IReadOnlyList<INetworkInterface> Available,
            int WatchCount,
            IReadOnlySet<INetworkInterface> Unresolved);
        private Snapshot _snapshot = new(0, Array.Empty<INetworkInterface>(), 0, new HashSet<INetworkInterface>());

        /// <summary>Reads the last published membership without refreshing or enforcing settings.</summary>
        public IReadOnlyList<INetworkInterface> GetAvailableInterfaces() => Volatile.Read(ref _snapshot).Available;
        internal bool IsUnresolved(INetworkInterface nic) => Volatile.Read(ref _snapshot).Unresolved.Contains(nic);
        internal int WatchCount => Volatile.Read(ref _snapshot).WatchCount;
        internal long Revision => Volatile.Read(ref _snapshot).Revision;

        /// <summary>The owner subscribes before starting and awaits the initial notification.</summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping.IsCancellationRequested) throw new InvalidOperationException("The interface monitor has stopped.");
            if (_processing is null)
            {
                _rules = configurations.Select(config => (config,
                    CreateMatcher().WithInterface(!string.IsNullOrWhiteSpace(config.Name) ? config.Name
                        : throw new ConfigurationValueException("A <NetworkInterface> needs a name selector.")))).ToList();
                Manager.InterfaceDetached += OnDetached;
                Manager.InterfaceAttached += OnAttached;
                Manager.Changed += OnChanged;
                _changed.Set();
                _processing = Task.Run(ProcessChangesAsync);
            }
            await _initialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Requests a fresh round, including after recovery without another OS notification.</summary>
        public void RequestReconciliation() => _changed.Set();

        private void OnChanged(object? sender, EventArgs args) => RequestReconciliation();
        private void OnAttached(object? sender, INetworkInterface nic) => RequestReconciliation();
        private void OnDetached(object? sender, INetworkInterface nic)
        {
            _detached.Enqueue(nic);
            RequestReconciliation();
        }

        private async Task ProcessChangesAsync()
        {
            try
            {
                while (true)
                {
                    await _changed.WaitAsync(_stopping.Token).ConfigureAwait(false);
                    _stopping.Token.ThrowIfCancellationRequested();
                    // Reset before work: changes during enforcement or an awaited subscriber
                    // leave the signal set and require one more round, without an event backlog.
                    _changed.Reset();
                    try
                    {
                        Reconcile();
                        _stopping.Token.ThrowIfCancellationRequested();
                        await Changed.InvokeAsync(this, EventArgs.Empty).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { break; }
                    catch (Exception ex) { Logger.LogError(ex, "Could not process network interface changes"); }
                    finally { _initialized.TrySetResult(); }
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            finally { _initialized.TrySetCanceled(_stopping.Token); }
        }

        private void Reconcile()
        {
            Manager.Refresh();
            while (_detached.TryDequeue(out var departed)) _watches.Remove(departed);
            var present = Manager.ToHashSet();
            foreach (var departed in _watches.Keys.Where(nic => !present.Contains(nic)).ToArray())
                _watches.Remove(departed);

            List<INetworkInterface> ready = [];
            HashSet<INetworkInterface> unresolved = [];
            foreach (var nic in present)
            {
                try
                {
                    if (!_watches.TryGetValue(nic, out var watch))
                    {
                        bool? disabled = null;
                        bool monitor = true;
                        NetworkInterfaceState allowed = NetworkInterfaceState.None;
                        foreach (var (config, matcher) in _rules)
                        {
                            if (!matcher.Matches(nic)) continue;
                            if (config.Disabled is bool value) disabled = value;
                            if (config.Monitor is bool include) monitor = include;
                            if (config.AllowToChange is { } changes) allowed = changes;
                        }
                        watch = new(nic, disabled, allowed, monitor);
                        _watches.Add(nic, watch);
                    }
                    watch.Reconcile();
                    if (watch.Monitor && !nic.IsDisabled) ready.Add(nic);
                }
                catch (Exception ex)
                {
                    unresolved.Add(nic);
                    Logger.LogError(ex, "Could not configure interface {Interface}", nic.Name);
                }
            }
            // Refresh operational metadata after writes without queuing our own successor.
            Manager.Refresh();
            present = Manager.ToHashSet();
            var available = ready.Where(present.Contains).ToArray();
            unresolved.IntersectWith(present);
            int watchCount = _watches.Keys.Count(present.Contains);
            Volatile.Write(ref _snapshot, new(Revision + 1, Array.AsReadOnly(available), watchCount, unresolved));
        }

        /// <summary>Stops enforcement and drains the current callback before context teardown.</summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return;
            Manager.Changed -= OnChanged;
            Manager.InterfaceDetached -= OnDetached;
            Manager.InterfaceAttached -= OnAttached;
            _stopping.Cancel();
            if (_processing is not null) await _processing.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            await StopAsync().ConfigureAwait(false);
            _stopping.Dispose();
            _disposed = true;
        }
    }
}
