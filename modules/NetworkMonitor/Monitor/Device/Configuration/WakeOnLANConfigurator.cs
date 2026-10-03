using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MadWizard.Desomnia.Network.Manager
{
    /**
     * On some platforms Wake-on-LAN cannot be enabled persistently.
     * Therefore the daemon will try to enable it each time, before going to sleep,
     * if not enabled otherwise.
     */
    internal class WakeOnLANConfigurator(WakeOnLANMode set, IWakeOnLANManager? manager = null) : INetworkService
    {
        public required ILogger<WakeOnLANConfigurator> Logger { private get; init; }

        public bool ShouldReplace { get; set; } = true;

        private WakeOnLANMode? _modesToReset;

        async Task INetworkService.BeforeSuspend()
        {
            if (manager is not null)
            {
                Logger.LogDebug("Automatically configuring Wake-on-LAN...");

                try
                {
                    var watch = Stopwatch.StartNew();

                    if (manager?.Modes is WakeOnLANMode modes && modes != set)
                    {
                        if (!manager.SupportedModes.HasFlag(set))
                        {
                            Logger.LogWarning("Wake-on-LAN (by {mode}) is not supported.", set);
                        }
                        else
                        {
                            var result = ShouldReplace ? manager.Modes = set : manager.Modes |= set;

                            Logger.LogDebug("Wake-on-LAN -> {mode} ({time} ms)", result, watch.ElapsedMilliseconds);

                            _modesToReset ??= modes;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Wake-on-LAN could not be configured.");
                }
            }
            else
            {
                Logger.LogWarning("Automatically enabling Wake-on-LAN is not possible ('ethtool' is not installed)");
            }
        }

        async Task INetworkService.Shutdown(NetworkShutdownReason reason)
        {
            if (_modesToReset is WakeOnLANMode reset)
            {
                Logger.LogDebug("Reverting Wake-on-LAN to its original state...");

                var watch = Stopwatch.StartNew();

                try
                {
                    manager?.Modes = reset;

                    Logger.LogDebug("Wake-on-LAN -> {mode} ({time} ms)", reset, watch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Wake-on-LAN could not be reverted.");
                }
                finally
                {
                    _modesToReset = null;
                }
            }
        }
    }
}