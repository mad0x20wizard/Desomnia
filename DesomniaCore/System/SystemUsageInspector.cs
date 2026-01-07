using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia
{
    public class SystemUsageInspector(SystemMonitor system) : BackgroundService
    {
        public required ILogger<SystemUsageInspector> Logger { protected get; init; }

        public required TimeSpan Interval { private get; init; }

        public UsageToken[] LastTokens { get; private set; } = [];

        public DateTime     LastTime { get; private set; } = DateTime.Now;
        public DateTime?    NextTime => LastTime + Interval;

        public event EventHandler? Inspected;

        private CancellationTokenSource _cancel = new();

        public void InspectNow()
        {
            _cancel.Cancel();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            system.Idle += LogInspectionResult;
            system.Demand += LogInspectionResult;

            Logger.LogDebug($"Checking resources every {Interval}");

            while (!stoppingToken.IsCancellationRequested)
            {
                _cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                try
                {
                    await Task.Delay(Interval, _cancel.Token);
                }
                catch (TaskCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                catch (TaskCanceledException)
                {
                    break;
                }

                try
                {
                    LastTokens = [.. system.Inspect(DateTime.Now - LastTime)];
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "ERROR");
                }
                finally
                {
                    LastTime = DateTime.Now;
                }

                Inspected?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task LogInspectionResult(Event eventObj)
        {
            if (eventObj is InspectionEvent usage)
            {
                Logger.LogInformation("{tokens} [{time} ms]", usage.Tokens, usage.Duration.TotalMilliseconds);
            }
        }
    }
}
