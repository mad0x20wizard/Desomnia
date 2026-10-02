using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Service.Duo.Manager.Watcher
{
    internal class PollingWatcher : IDuoWatcher
    {
        public required ILogger<PollingWatcher> Logger { protected get; init; }

        public required TimeSpan PollInterval { get; set; }

        async IAsyncEnumerable<WatchSignal> IDuoWatcher.WatchAsync(IEnumerable<DuoInstance> instances, [EnumeratorCancellation] CancellationToken token)
        {
            Logger.LogDebug("Polling Duo instances every {Interval}", PollInterval);

            while (!token.IsCancellationRequested)
            {
                yield return new WatchSignal();

                await Task.Delay(PollInterval, token);
            }
        }
    }
}
