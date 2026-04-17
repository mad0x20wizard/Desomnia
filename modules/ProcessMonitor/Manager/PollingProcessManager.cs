using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Process.Manager
{
    /**
     * Fallback, if OS doesn't support event-based process start/stop notifications.
     */
    public class PollingProcessManager(TimeSpan interval) : ListenerAwareProcessManager, IHostedService, IDisposable
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

        public virtual void Dispose()
        {
            _stoppingCts?.Cancel();
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
