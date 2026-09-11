using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class PollingWatcher : Watcher
    {
        public required TimeSpan PollInterval { get; set; }

        public override async Task WatchAsync(IEnumerable<DuoInstance> instances, CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var timeout = stoppingToken.WithTimeout(PollInterval);

                    try
                    {
                        await RefreshInstances(instances, timeout.Token);
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
