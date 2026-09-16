using MadWizard.Desomnia.Events;
using MadWizard.Desomnia.Network.Configuration.Options;
using MadWizard.Desomnia.Network.Neighborhood;
using PacketDotNet;

namespace MadWizard.Desomnia.Network.Watch
{
    public class NetworkServiceWatch(NetworkService service) : NetworkWatch<Resource>
    {
        public NetworkService Service => service;

        public virtual bool ShouldHandoffToSleepProxy { get; set; } = true;

        public AdvertiseOptions     AdvertiseOptions    { get; init; }
        public KnockOptions?        KnockOptions        { get; init; }

        public bool CanTriggerDemand(Packet trigger)
        {
            return IsIdle && ((IEventSystem)this)[nameof(Demand)].HasHandlers && Service.Accepts(trigger);
        }

        protected internal override void ReportNetworkTraffic(EthernetPacket packet, PacketDirection direction)
        {
            if (Service.Accepts(packet, direction))
            {
                base.ReportNetworkTraffic(packet, direction);
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
