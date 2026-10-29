using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;

namespace MadWizard.Desomnia.Network.Watch
{
    public abstract class NetworkHostWatch : NetworkWatch<NetworkServiceWatch>
    {
        public required ILogger<NetworkHostWatch> Logger { protected get; init; }

        public required NetworkHost Host { get; init; }

        public event EventInvocation? Started;
        public event EventInvocation? Suspended;
        public event EventInvocation? Stopped;

        public event EventInvocation? MagicPacket;

        public void TriggerStarted()    { Logger.LogDebug($"'{Host.Name}' changed state to: running");      Started.TriggerEvent(); }
        public void TriggerSuspended()  { Logger.LogDebug($"'{Host.Name}' changed state to: suspended");    Suspended.TriggerEvent(); }
        public void TriggerStopped()    { Logger.LogDebug($"'{Host.Name}' changed state to: stopped");      Stopped.TriggerEvent(); }

        public NetworkServiceWatch? this[NetworkService? service] => this.FirstOrDefault(watch => watch.Service == service);

        internal protected override async Task StartWatch()
        {
            foreach (var service in this.Where(s => !s.IsWatching))
                await service.StartWatch();

            await base.StartWatch();
        }

        protected internal override void ReportNetworkTraffic(IFilterContext context)
        {
            foreach (var watch in this)
            {
                watch.ReportNetworkTraffic(context);
            }

            base.ReportNetworkTraffic(context);
        }

        internal protected override async Task StopWatch(bool gracefully)
        {
            foreach (var service in this.Where(s => s.IsWatching))
                await service.StopWatch(gracefully);

            await base.StopWatch(gracefully);
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (HadThresholdTraffic(interval, out long bytes))
            {
                var usage = new NetworkHostUsage(Host, bytes);

                // summarize tokens
                foreach (var serviceToken in base.InspectResource(interval))
                    if (serviceToken is NetworkServiceUsage service)
                        usage.Tokens.Add(service);

                if (usage.Tokens.Any() || Host is not LocalHost)
                    yield return usage;
            }
        }
    }
}
