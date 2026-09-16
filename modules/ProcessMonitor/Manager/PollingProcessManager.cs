using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Processes.Manager
{
    /**
     * Fallback, if OS doesn't support event-based process start/stop notifications.
     */
    public class PollingProcessManager(TimeSpan interval) : ListenerAwareProcessManager, IHostedService
    {
        private DateTimeOffset _lastRefresh;

        #region BackgroundService
        private Task? _executeTask;
        private CancellationTokenSource? _stoppingCts;

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _executeTask = Task.Run(() => ExecuteAsync(_stoppingCts.Token), _stoppingCts.Token);

            return Task.CompletedTask;
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_executeTask == null)
                return;

            try
            {
                _stoppingCts!.Cancel();
            }
            finally
            {
                await _executeTask.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        public override void Dispose()
        {
            _stoppingCts?.Cancel();

            base.Dispose();
        }
        #endregion

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogDebug("Polling processes every {interval} ms", interval.TotalMilliseconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);

                    if (ListenerCount > 0) // does anybody care?
                    {
                        RefreshProcessList();
                    }
                }
                catch (TaskCanceledException)
                {
                    break; // stop polling
                }
                catch (Exception ex)
                {
                    // One bad poll must not end the loop. An enumeration that fails – transiently,
                    // or because the platform is not allowed to ask – would otherwise leave the
                    // task faulted and every process watch frozen on its last known roster, with
                    // nothing but this log line to say so.
                    Logger.LogWarning(ex, "Failed to refresh the process list");
                }
            }
        }

        protected override void RefreshProcessList()
        {
            base.RefreshProcessList();

            _lastRefresh = DateTimeOffset.Now;
        }

        public override IEnumerator<IProcess> GetEnumerator()
        {
            if (!Monitor.IsEntered(this) && (DateTimeOffset.Now - _lastRefresh) > interval)
                RefreshProcessList();

            return base.GetEnumerator();
        }
    }
}
