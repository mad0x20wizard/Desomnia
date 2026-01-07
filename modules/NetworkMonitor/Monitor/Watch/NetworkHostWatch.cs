using MadWizard.Desomnia.Network.Demand;
using MadWizard.Desomnia.Network.Neighborhood;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace MadWizard.Desomnia.Network
{
    public abstract class NetworkHostWatch : NetworkWatch<NetworkServiceWatch>
    {
        public required ILogger<NetworkHostWatch> Logger { protected get; init; }

        public required NetworkHost Host { get; init; }

        public event EventInvocation? Started;
        public event EventInvocation? Suspended;
        public event EventInvocation? Stopped;

        public event EventInvocation? MagicPacket;

        public void TriggerStarted()    { Logger.LogDebug($"'{Host.Name}' changed state to: running");      TriggerEvent(nameof(Started)); }
        public void TriggerSuspended()  { Logger.LogDebug($"'{Host.Name}' changed state to: suspended");    TriggerEvent(nameof(Suspended)); }
        public void TriggerStopped()    { Logger.LogDebug($"'{Host.Name}' changed state to: stopped");      TriggerEvent(nameof(Stopped)); }

        public NetworkServiceWatch? this[NetworkService? service] => this.Where(watch => watch.Service == service).FirstOrDefault();

        protected override bool ShouldInspectResource(NetworkServiceWatch service) => !service.IsHidden;

        protected internal override void ReportNetworkTraffic(EthernetPacket packet)
        {
            foreach (var watch in this)
            {
                watch.ReportNetworkTraffic(packet);
            }

            base.ReportNetworkTraffic(packet);
        }

        protected void ReportNetworkTraffic(DemandEvent @event)
        {
            foreach (var packet in @event)
            {
                ReportNetworkTraffic(packet);
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (HadThresholdTraffic(interval, out long bytes))
            {
                var token = new NetworkHostUsage(Host, bytes);

                // summarize tokens
                foreach (var serviceToken in base.InspectResource(interval))
                    if (serviceToken is NetworkServiceUsage service)
                        token.Tokens.Add(service);

                yield return token;
            }
        }
    }
}
