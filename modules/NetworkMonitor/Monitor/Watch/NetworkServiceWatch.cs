using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Capture;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Filter;
using MadWizard.Desomnia.Network.Neighborhood;
using System.Net;

namespace MadWizard.Desomnia.Network.Watch
{
    public class NetworkServiceWatch(NetworkService service) : NetworkWatch<Resource>
    {
        public NetworkService Service => service;

        public required Lazy<IFilter> Filter { get; init; }

        public virtual bool ShouldHandoffToSleepProxy { get; set; } = true;

        public AdvertiseOptions     AdvertiseOptions    { get; init; }
        public KnockOptions?        KnockOptions        { get; init; }

        public bool CanTriggerDemand(INetworkCapture trigger)
        {
            if (IsIdle && ((IEventSystem)this)[nameof(Demand)].HasHandlers)
            {
                if (trigger.TargetEndPoint is IPServiceEndPoint ips)
                {
                    return Service.Contains(ips);
                }
            }

            return false;
        }

        protected internal override void ReportNetworkTraffic(IFilterContext context)
        {
            if (context is IIPServiceFilterContext ips)
            {
                if (Service.Contains(ips.Service))
                {
                    base.ReportNetworkTraffic(context);
                }
            }
        }

        protected override IEnumerable<UsageToken> InspectResource(TimeSpan interval)
        {
            if (HadThresholdTraffic(interval, out long bytes))
            {
                yield return new NetworkServiceUsage(Service, bytes) { Rate = ThresholdRate(interval, bytes) };
            }
        }
    }
}
