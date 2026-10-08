using MadWizard.Desomnia.Network.Context;
using MadWizard.Desomnia.Network.Interface.Configuration;
using MadWizard.Desomnia.Network.Manager;
using Microsoft.Extensions.Logging;
using Nito.Disposables;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MadWizard.Desomnia.Network.Interface
{
    internal class InterfaceConfigurator(IEnumerable<Configurator> configurators) : INetworkService
    {
        public required ILogger<InterfaceConfigurator> Logger { private get; init; }

        public required NetworkContext Context { private get; init; }
        public required INetworkInterfaceManager Manager { private get; init; }

        private readonly Dictionary<Configurator, object> _needReset = [];

        async Task INetworkService.BeforeSuspend()
        {
            Dictionary<Configurator, object> changes = []; // planning what should change

            foreach (var configurator in configurators)
            {
                try
                {
                    var original = configurator.ReadRawConfiguration();

                    if (!Equals(original, configurator.Target))
                    {
                        changes[configurator] = configurator.Target;

                        _needReset.TryAdd(configurator, original); // only remember old value once
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Cannot read current config of {Configurator}", configurator.GetType().Name);
                }
            }

            if (changes.Count > 0)
            {
                Logger.LogDebug("Reconfiguring network interface...");

                await ApplyConfiguration(changes);
            }
        }

        private async Task ApplyConfiguration(IEnumerable<KeyValuePair<Configurator, object>> changes, bool mustRecover = true)
        {
            await using var scope = BeginConfiguration(mustRecover);

            foreach (var (configurator, value) in changes)
            {
                try
                {
                    Logger.LogDebug("{Configurator} -> {Configuration}",
                        configurator.GetType().Name, value);

                    await configurator.ApplyConfiguration(value);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not apply {Configurator}", configurator.GetType().Name);
                }
            }
        }

        async Task INetworkService.Shutdown(NetworkShutdownReason reason)
        {
            if (reason != NetworkShutdownReason.InterfaceDisconnected && _needReset.Count > 0)
            {
                Logger.LogDebug("Reverting network interface...");

                try
                {
                    await ApplyConfiguration(_needReset.Reverse(), false);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not revert interface");
                }
                finally
                {
                    _needReset.Clear();
                }
            }
        }

        private IAsyncDisposable BeginConfiguration(bool mustRecover)
        {
            Stopwatch watch = Stopwatch.StartNew();
            var addressKinds = Context.Interface.Addresses.Where(IsUsable).Select(a => AddressKind(a.Address)).ToHashSet();
            var gateways = Context.Interface.Gateways.Select(a => a.Address.AddressFamily).ToHashSet();

            // The observer owns lifecycle serialization; this scope only owns the NIC batch.
            Context.Device.StopCapture();

            return AsyncDisposable.Create(async () =>
            {
                // Teardown restores the settings but must not restart capture.
                if (!mustRecover) return;
                try
                {
                    Logger.LogDebug("Waiting for network interface and capture recovery...");
                    await InterfaceRecovery.Wait(ReadReadiness, Context.Device.Restart,
                        TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100),
                        () => Context.Device.IsCapturing);
                    Logger.LogDebug("Interface configuration completed ({Elapsed} ms)", watch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Network interface did not recover");
                    throw;
                }
            });

            InterfaceReadiness ReadReadiness()
            {
                Manager.Refresh(); // NetworkChange is a hint; its last snapshot may be stale.
                var nic = Manager[Context.Interface.Identity];
                if (nic is null || nic.Status != OperationalStatus.Up) return new(false, "down/absent");
                var addresses = nic.Addresses.Where(IsUsable).ToArray();
                bool ready = addressKinds.IsSubsetOf(addresses.Select(a => AddressKind(a.Address)))
                    && gateways.IsSubsetOf(nic.Gateways.Select(a => a.Address.AddressFamily));
                return new(ready, string.Join(";", addresses.Select(a => a.Address.ToString())
                    .Concat(nic.Gateways.Select(g => "gateway:" + g.Address)).Order()));
            }
        }

        private static bool IsUsable(UnicastIPAddressInformation address) => !OperatingSystem.IsWindows()
            || address.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred;

        private static string AddressKind(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return bytes[0] == 169 && bytes[1] == 254 ? "IPv4-linklocal" : "IPv4";
            return address.IsIPv6LinkLocal ? "IPv6-linklocal" : (bytes[0] & 0xfe) == 0xfc ? "IPv6-ULA" : "IPv6";
        }
        internal readonly record struct InterfaceReadiness(bool Ready, string Version);

        internal static class InterfaceRecovery
        {
            // A short stable-state interval is a heuristic for drivers with no completion signal.
            // It is not a guarantee against a later restart, and we do not wait for a Down event
            // (some setters never restart the NIC). The timeout bounds retries, not native calls.
            internal static async Task Wait(Func<InterfaceReadiness> read, Action reopen,
                TimeSpan timeout, TimeSpan stability, TimeSpan interval, Func<bool>? captureHealthy = null)
            {
                var timer = Stopwatch.StartNew();
                TimeSpan? stableSince = null;
                string? version = null;
                Exception? failure = null;
                while (timer.Elapsed < timeout)
                {
                    try
                    {
                        var state = read();
                        if (!state.Ready) stableSince = null;
                        else
                        {
                            if (stableSince is null || version != state.Version) stableSince = timer.Elapsed;
                            if (timer.Elapsed - stableSince >= stability)
                            {
                                reopen();
                                await Task.Delay(interval); // allow an asynchronous capture-start failure to surface
                                // Don't declare readiness if opening overlapped another observed change.
                                if (read() is var after && after.Ready && after.Version == state.Version
                                    && (captureHealthy?.Invoke() ?? true)) return;
                                stableSince = null;
                            }
                        }
                        version = state.Version;
                    }
                    catch (Exception ex) { failure = ex; stableSince = null; }
                    await Task.Delay(interval);
                }
                throw new TimeoutException("Network interface/capture did not recover within " + timeout, failure);
            }
        }

    }
}
