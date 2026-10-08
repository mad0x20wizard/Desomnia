using MadWizard.Desomnia.Network.Watch;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Handoff
{
    public class HandoffService(NetworkMonitor monitor) : INetworkService
    {
        public required ILogger<HandoffService> Logger { private get; init; }

        async Task INetworkService.Startup() { }

        async Task INetworkService.BeforeSuspend()
        {
            await HandoffLocalWatches(); // throw an exception if a handoff failed, but was required -> suspend cancelled
        }

        async Task INetworkService.Suspend()
        {
            try
            {
                await HandoffLocalWatches();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not handoff local watches");
            }
        }

        private async Task HandoffLocalWatches()
        {
            var watches = monitor.OfType<LocalHostWatch>().Where(watch => watch.HandoffPending).ToList();

            try
            {
                if (watches.Where(watch => watch is not LocalVirtualHostWatch) is var watchesPhysical && watchesPhysical.Any())
                {
                    Logger.LogDebug("Attempt to handoff local watch...");

                    foreach (var watch in watchesPhysical)
                    {
                        await HandoffLocalWatch(watch);
                    }
                }

                if (watches.OfType<LocalVirtualHostWatch>() is var watchesVirtual && watchesVirtual.Any())
                {
                    Logger.LogDebug("Attempt to handoff {Count} local virtual watch(es)...", watchesVirtual.Count());

                    await Task.WhenAll(watchesVirtual.Select(HandoffLocalWatch));
                }
            }
            catch
            {
                await Task.WhenAll(watches.Select(ReclaimLocalWatch)); throw; // reclaim all watches, before aborting
            }
        }

        private async Task HandoffLocalWatch(LocalHostWatch watch)
        {
            using var scope = Logger.BeginHostScope(watch.Host);

            Logger.LogDebug("Handing off local watch for '{Host}'...", watch.Host.Name);

            try
            {
                await watch.HandoffWatch();
            }
            catch (Exception ex)
            {
                Logger.Log(watch.HandoffOptions.IsMandatory ? LogLevel.Error : LogLevel.Warning,
                    ex, "Could not handoff watch for '{Host}'", watch.Host.Name);

                if (watch.HandoffOptions.IsMandatory)
                {
                    throw;
                }
            }
        }

        private async Task ReclaimLocalWatch(LocalHostWatch watch)
        {
            using var scope = Logger.BeginHostScope(watch.Host);

            try
            {
                await watch.ReclaimWatch();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not reclaim watch for '{Host}'.", watch.Host.Name);
            }
        }

        async Task INetworkService.Resume()
        {
            foreach (var watch in monitor.OfType<LocalHostWatch>())
            {
                await ReclaimLocalWatch(watch);
            }
        }

        async Task INetworkService.Shutdown(NetworkShutdownReason reason) { }
    }
}
