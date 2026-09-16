using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class PollingWatcher : BaseWatcher
    {
        public required TimeSpan PollInterval { get; set; }

        public override async Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken stoppingToken)
        {
            Logger.LogDebug("Polling Duo instances every {Interval}", PollInterval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RefreshInstances(instances, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        Logger.LogError(ex, "Error checking Duo instances.");
                    }

                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal hosted-service shutdown.
            }
        }
    }
}
